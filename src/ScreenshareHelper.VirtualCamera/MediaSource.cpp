#include "MediaSource.h"
#include "Shared.h"
#include <sddl.h>
#include <cstring>

#define RETURN_IF_FAILED(expr) do { HRESULT _hr = (expr); if (FAILED(_hr)) return _hr; } while (0)

// KSCATEGORY / pin category for a video capture pin (ks.h: PINNAME_VIDEO_CAPTURE)
static const GUID PINNAME_VIDEO_CAPTURE_GUID = { 0xfb6c4281, 0x0353, 0x11d1, { 0x90, 0x5f, 0x00, 0x00, 0xc0, 0xcc, 0x16, 0xba } };

#pragma region SharedFrameReader

SharedFrameReader::~SharedFrameReader()
{
    if (m_view) UnmapViewOfFile(m_view);
    if (m_mapping) CloseHandle(m_mapping);
}

void SharedFrameReader::EnsureOpen()
{
    if (m_view)
        return;

    // created here (camera service, session 0) so the app in the user session can open it;
    // creating global objects from a user session would need SeCreateGlobalPrivilege
    PSECURITY_DESCRIPTOR sd = nullptr;
    // SYSTEM + LocalService full access, authenticated users read/write
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(L"D:P(A;;GA;;;SY)(A;;GA;;;LS)(A;;GRGW;;;AU)", SDDL_REVISION_1, &sd, nullptr))
        return;
    SECURITY_ATTRIBUTES sa = { sizeof(sa), sd, FALSE };
    m_mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, &sa, PAGE_READWRITE, 0, (DWORD)VCAM_SHARED_SIZE, VCAM_SHARED_MEMORY_NAME);
    LocalFree(sd);
    if (!m_mapping)
        return;
    bool created = GetLastError() != ERROR_ALREADY_EXISTS;

    m_view = (BYTE*)MapViewOfFile(m_mapping, FILE_MAP_ALL_ACCESS, 0, 0, VCAM_SHARED_SIZE);
    if (!m_view)
        return;

    auto header = (VCamHeader*)m_view;
    if (created || header->magic != VCAM_MAGIC)
    {
        header->width = VCAM_WIDTH;
        header->height = VCAM_HEIGHT;
        header->latestSlot = -1;
        header->frameCounter = 0;
        MemoryBarrier();
        header->magic = VCAM_MAGIC;
    }
}

bool SharedFrameReader::CopyLatest(BYTE* dst)
{
    EnsureOpen();
    if (!m_view)
        return false;

    auto header = (VCamHeader*)m_view;
    LONG slot = header->latestSlot;
    if (slot < 0 || slot > 1)
        return false;
    MemoryBarrier();
    memcpy(dst, m_view + sizeof(VCamHeader) + slot * VCAM_SLOT_SIZE, VCAM_SLOT_SIZE);
    return true;
}

#pragma endregion

#pragma region MediaStream

static HRESULT CreateVideoType(const GUID& subtype, IMFMediaType** result)
{
    ComPtr<IMFMediaType> type;
    RETURN_IF_FAILED(MFCreateMediaType(&type));
    bool nv12 = subtype == MFVideoFormat_NV12;
    RETURN_IF_FAILED(type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
    RETURN_IF_FAILED(type->SetGUID(MF_MT_SUBTYPE, subtype));
    RETURN_IF_FAILED(MFSetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, VCAM_WIDTH, VCAM_HEIGHT));
    RETURN_IF_FAILED(MFSetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, VCAM_FPS, 1));
    RETURN_IF_FAILED(MFSetAttributeRatio(type.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
    RETURN_IF_FAILED(type->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
    RETURN_IF_FAILED(type->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE));
    RETURN_IF_FAILED(type->SetUINT32(MF_MT_FIXED_SIZE_SAMPLES, TRUE));
    RETURN_IF_FAILED(type->SetUINT32(MF_MT_DEFAULT_STRIDE, nv12 ? VCAM_WIDTH : VCAM_WIDTH * 4)); // positive = top-down
    RETURN_IF_FAILED(type->SetUINT32(MF_MT_SAMPLE_SIZE, nv12 ? VCAM_WIDTH * VCAM_HEIGHT * 3 / 2 : VCAM_WIDTH * VCAM_HEIGHT * 4));
    *result = type.Detach();
    return S_OK;
}

HRESULT MediaStream::RuntimeClassInitialize(MediaSource* source)
{
    m_source = source;
    m_bgra.reset(new BYTE[VCAM_SLOT_SIZE]);
    RETURN_IF_FAILED(MFCreateEventQueue(&m_queue));

    ComPtr<IMFMediaType> types[2];
    RETURN_IF_FAILED(CreateVideoType(MFVideoFormat_NV12, &types[0]));
    RETURN_IF_FAILED(CreateVideoType(MFVideoFormat_RGB32, &types[1]));
    IMFMediaType* raw[2] = { types[0].Get(), types[1].Get() };
    RETURN_IF_FAILED(MFCreateStreamDescriptor(0, 2, raw, &m_descriptor));

    ComPtr<IMFMediaTypeHandler> handler;
    RETURN_IF_FAILED(m_descriptor->GetMediaTypeHandler(&handler));
    RETURN_IF_FAILED(handler->SetCurrentMediaType(types[0].Get()));

    RETURN_IF_FAILED(MFCreateAttributes(&m_attributes, 4));
    for (IMFAttributes* a : { m_attributes.Get(), static_cast<IMFAttributes*>(m_descriptor.Get()) })
    {
        RETURN_IF_FAILED(a->SetGUID(MF_DEVICESTREAM_STREAM_CATEGORY, PINNAME_VIDEO_CAPTURE_GUID));
        RETURN_IF_FAILED(a->SetUINT32(MF_DEVICESTREAM_STREAM_ID, 0));
        RETURN_IF_FAILED(a->SetUINT32(MF_DEVICESTREAM_FRAMESERVER_SHARED, 1));
        RETURN_IF_FAILED(a->SetUINT32(MF_DEVICESTREAM_ATTRIBUTE_FRAMESOURCE_TYPES, MFFrameSourceTypes_Color));
    }
    return S_OK;
}

STDMETHODIMP MediaStream::GetEvent(DWORD flags, IMFMediaEvent** event)
{
    ComPtr<IMFMediaEventQueue> queue;
    {
        std::lock_guard<std::mutex> lock(m_lock);
        if (m_shutdown) return MF_E_SHUTDOWN;
        queue = m_queue;
    }
    return queue->GetEvent(flags, event); // may block, so not under the lock
}

STDMETHODIMP MediaStream::BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state)
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    return m_queue->BeginGetEvent(callback, state);
}

STDMETHODIMP MediaStream::EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event)
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    return m_queue->EndGetEvent(result, event);
}

STDMETHODIMP MediaStream::QueueEvent(MediaEventType type, REFGUID extType, HRESULT status, const PROPVARIANT* eventValue)
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    return m_queue->QueueEventParamVar(type, extType, status, eventValue);
}

STDMETHODIMP MediaStream::GetMediaSource(IMFMediaSource** source)
{
    if (!source) return E_POINTER;
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    return m_source->QueryInterface(IID_PPV_ARGS(source));
}

STDMETHODIMP MediaStream::GetStreamDescriptor(IMFStreamDescriptor** descriptor)
{
    if (!descriptor) return E_POINTER;
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    return m_descriptor.CopyTo(descriptor);
}

static inline BYTE Clamp(int v) { return (BYTE)(v < 0 ? 0 : v > 255 ? 255 : v); }

HRESULT MediaStream::FillBuffer(BYTE* data, const GUID& subtype)
{
    if (m_reader.CopyLatest(m_bgra.get()))
        m_hasFrame = true;
    else if (!m_hasFrame)
        memset(m_bgra.get(), 0x20, VCAM_SLOT_SIZE); // dark gray until the app delivers the first frame

    const BYTE* src = m_bgra.get();
    if (subtype == MFVideoFormat_RGB32)
    {
        memcpy(data, src, VCAM_SLOT_SIZE);
        return S_OK;
    }

    // BGRA -> NV12 (BT.601, limited range)
    const UINT32 w = VCAM_WIDTH, h = VCAM_HEIGHT;
    BYTE* yPlane = data;
    BYTE* uvPlane = data + w * h;
    for (UINT32 y = 0; y < h; y += 2)
    {
        const BYTE* row0 = src + size_t(y) * w * 4;
        const BYTE* row1 = row0 + size_t(w) * 4;
        BYTE* y0 = yPlane + size_t(y) * w;
        BYTE* y1 = y0 + w;
        BYTE* uv = uvPlane + size_t(y / 2) * w;
        for (UINT32 x = 0; x < w; x += 2)
        {
            int rs = 0, gs = 0, bs = 0;
            const BYTE* px[4] = { row0 + x * 4, row0 + x * 4 + 4, row1 + x * 4, row1 + x * 4 + 4 };
            BYTE* out[4] = { y0 + x, y0 + x + 1, y1 + x, y1 + x + 1 };
            for (int i = 0; i < 4; i++)
            {
                int b = px[i][0], g = px[i][1], r = px[i][2];
                *out[i] = Clamp(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
                rs += r; gs += g; bs += b;
            }
            rs /= 4; gs /= 4; bs /= 4;
            uv[x] = Clamp(((-38 * rs - 74 * gs + 112 * bs + 128) >> 8) + 128);
            uv[x + 1] = Clamp(((112 * rs - 94 * gs - 18 * bs + 128) >> 8) + 128);
        }
    }
    return S_OK;
}

STDMETHODIMP MediaStream::RequestSample(IUnknown* token)
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    if (m_state != MF_STREAM_STATE_RUNNING) return MF_E_MEDIA_SOURCE_WRONGSTATE;

    ComPtr<IMFMediaTypeHandler> handler;
    ComPtr<IMFMediaType> type;
    GUID subtype = MFVideoFormat_NV12;
    if (SUCCEEDED(m_descriptor->GetMediaTypeHandler(&handler)) && SUCCEEDED(handler->GetCurrentMediaType(&type)))
        type->GetGUID(MF_MT_SUBTYPE, &subtype);

    DWORD size = subtype == MFVideoFormat_RGB32 ? (DWORD)VCAM_SLOT_SIZE : VCAM_WIDTH * VCAM_HEIGHT * 3 / 2;
    ComPtr<IMFSample> sample;
    ComPtr<IMFMediaBuffer> buffer;
    RETURN_IF_FAILED(MFCreateSample(&sample));
    RETURN_IF_FAILED(MFCreateMemoryBuffer(size, &buffer));

    BYTE* data = nullptr;
    RETURN_IF_FAILED(buffer->Lock(&data, nullptr, nullptr));
    HRESULT hr = FillBuffer(data, subtype);
    buffer->Unlock();
    RETURN_IF_FAILED(hr);
    RETURN_IF_FAILED(buffer->SetCurrentLength(size));
    RETURN_IF_FAILED(sample->AddBuffer(buffer.Get()));

    RETURN_IF_FAILED(sample->SetSampleTime(MFGetSystemTime()));
    RETURN_IF_FAILED(sample->SetSampleDuration(10'000'000 / VCAM_FPS));
    if (token)
        RETURN_IF_FAILED(sample->SetUnknown(MFSampleExtension_Token, token));

    return m_queue->QueueEventParamUnk(MEMediaSample, GUID_NULL, S_OK, sample.Get());
}

STDMETHODIMP MediaStream::SetStreamState(MF_STREAM_STATE state)
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    m_state = state;
    return S_OK;
}

STDMETHODIMP MediaStream::GetStreamState(MF_STREAM_STATE* state)
{
    if (!state) return E_POINTER;
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    *state = m_state;
    return S_OK;
}

HRESULT MediaStream::Start()
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    m_state = MF_STREAM_STATE_RUNNING;
    return m_queue->QueueEventParamVar(MEStreamStarted, GUID_NULL, S_OK, nullptr);
}

HRESULT MediaStream::Stop()
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    m_state = MF_STREAM_STATE_STOPPED;
    return m_queue->QueueEventParamVar(MEStreamStopped, GUID_NULL, S_OK, nullptr);
}

void MediaStream::Shutdown()
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return;
    m_shutdown = true;
    m_queue->Shutdown();
}

#pragma endregion

#pragma region MediaSource

HRESULT MediaSource::RuntimeClassInitialize(IMFAttributes* activateAttributes)
{
    RETURN_IF_FAILED(MFCreateEventQueue(&m_queue));
    RETURN_IF_FAILED(MFCreateAttributes(&m_attributes, 4));
    if (activateAttributes)
        RETURN_IF_FAILED(activateAttributes->CopyAllItems(m_attributes.Get()));

    RETURN_IF_FAILED(wrl::MakeAndInitialize<MediaStream>(&m_stream, this));
    IMFStreamDescriptor* descriptors[1] = { m_stream->Descriptor() };
    RETURN_IF_FAILED(MFCreatePresentationDescriptor(1, descriptors, &m_presentation));
    RETURN_IF_FAILED(m_presentation->SelectStream(0));
    return S_OK;
}

STDMETHODIMP MediaSource::GetEvent(DWORD flags, IMFMediaEvent** event)
{
    ComPtr<IMFMediaEventQueue> queue;
    {
        std::lock_guard<std::mutex> lock(m_lock);
        if (m_shutdown) return MF_E_SHUTDOWN;
        queue = m_queue;
    }
    return queue->GetEvent(flags, event);
}

STDMETHODIMP MediaSource::BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state)
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    return m_queue->BeginGetEvent(callback, state);
}

STDMETHODIMP MediaSource::EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event)
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    return m_queue->EndGetEvent(result, event);
}

STDMETHODIMP MediaSource::QueueEvent(MediaEventType type, REFGUID extType, HRESULT status, const PROPVARIANT* eventValue)
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    return m_queue->QueueEventParamVar(type, extType, status, eventValue);
}

STDMETHODIMP MediaSource::GetCharacteristics(DWORD* characteristics)
{
    if (!characteristics) return E_POINTER;
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    *characteristics = MFMEDIASOURCE_IS_LIVE;
    return S_OK;
}

STDMETHODIMP MediaSource::CreatePresentationDescriptor(IMFPresentationDescriptor** descriptor)
{
    if (!descriptor) return E_POINTER;
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    return m_presentation->Clone(descriptor);
}

STDMETHODIMP MediaSource::Start(IMFPresentationDescriptor* descriptor, const GUID* timeFormat, const PROPVARIANT* /*startPosition*/)
{
    if (!descriptor) return E_INVALIDARG;
    if (timeFormat && *timeFormat != GUID_NULL) return MF_E_UNSUPPORTED_TIME_FORMAT;

    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;

    DWORD count = 0;
    RETURN_IF_FAILED(descriptor->GetStreamDescriptorCount(&count));
    for (DWORD i = 0; i < count; i++)
    {
        BOOL selected = FALSE;
        ComPtr<IMFStreamDescriptor> sd;
        RETURN_IF_FAILED(descriptor->GetStreamDescriptorByIndex(i, &selected, &sd));
        if (!selected)
        {
            m_stream->Stop();
            continue;
        }

        RETURN_IF_FAILED(m_queue->QueueEventParamUnk(m_streamAnnounced ? MEUpdatedStream : MENewStream,
            GUID_NULL, S_OK, static_cast<IMFMediaStream*>(m_stream.Get())));
        m_streamAnnounced = true;
        RETURN_IF_FAILED(m_stream->Start());
    }

    PROPVARIANT start;
    PropVariantInit(&start);
    start.vt = VT_I8;
    start.hVal.QuadPart = MFGetSystemTime();
    return m_queue->QueueEventParamVar(MESourceStarted, GUID_NULL, S_OK, &start);
}

STDMETHODIMP MediaSource::Stop()
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    m_stream->Stop();
    return m_queue->QueueEventParamVar(MESourceStopped, GUID_NULL, S_OK, nullptr);
}

STDMETHODIMP MediaSource::Pause()
{
    return MF_E_INVALID_STATE_TRANSITION; // live source, no pause
}

STDMETHODIMP MediaSource::Shutdown()
{
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    m_shutdown = true;
    m_stream->Shutdown();
    m_queue->Shutdown();
    return S_OK;
}

STDMETHODIMP MediaSource::GetSourceAttributes(IMFAttributes** attributes)
{
    if (!attributes) return E_POINTER;
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    return m_attributes.CopyTo(attributes);
}

STDMETHODIMP MediaSource::GetStreamAttributes(DWORD streamId, IMFAttributes** attributes)
{
    if (!attributes) return E_POINTER;
    std::lock_guard<std::mutex> lock(m_lock);
    if (m_shutdown) return MF_E_SHUTDOWN;
    if (streamId != 0) return MF_E_INVALIDSTREAMNUMBER;
    *attributes = m_stream->Attributes();
    (*attributes)->AddRef();
    return S_OK;
}

STDMETHODIMP MediaSource::SetD3DManager(IUnknown*)
{
    return S_OK; // frames are delivered in system memory
}

STDMETHODIMP MediaSource::GetService(REFGUID, REFIID, LPVOID* object)
{
    if (!object) return E_POINTER;
    *object = nullptr;
    return MF_E_UNSUPPORTED_SERVICE;
}

STDMETHODIMP MediaSource::KsProperty(PKSPROPERTY, ULONG, LPVOID, ULONG, ULONG*)
{
    return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
}

STDMETHODIMP MediaSource::KsMethod(PKSMETHOD, ULONG, LPVOID, ULONG, ULONG*)
{
    return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
}

STDMETHODIMP MediaSource::KsEvent(PKSEVENT, ULONG, LPVOID, ULONG, ULONG*)
{
    return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
}

#pragma endregion

#pragma region Activate

HRESULT Activate::RuntimeClassInitialize()
{
    RETURN_IF_FAILED(MFCreateAttributes(&m_attributes, 4));
    return S_OK;
}

STDMETHODIMP Activate::ActivateObject(REFIID riid, void** object)
{
    if (!object) return E_POINTER;
    if (!m_source)
        RETURN_IF_FAILED(wrl::MakeAndInitialize<MediaSource>(&m_source, m_attributes.Get()));
    return m_source->QueryInterface(riid, object);
}

STDMETHODIMP Activate::ShutdownObject()
{
    if (m_source)
        m_source->Shutdown();
    return S_OK;
}

STDMETHODIMP Activate::DetachObject()
{
    m_source.Reset();
    return S_OK;
}

#pragma endregion
