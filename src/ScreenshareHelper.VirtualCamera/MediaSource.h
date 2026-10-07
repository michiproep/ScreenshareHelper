#pragma once
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mferror.h>
#include <ks.h>
#include <ksproxy.h>
#include <wrl.h>
#include <mutex>
#include "AttributesImpl.h"

using Microsoft::WRL::ComPtr;
namespace wrl = Microsoft::WRL;

class MediaSource;

/// Reads the newest BGRA frame the app put into shared memory.
class SharedFrameReader
{
public:
    ~SharedFrameReader();
    /// Copies the newest frame (VCAM_WIDTH x VCAM_HEIGHT BGRA) into dst; false if the app hasn't sent one yet.
    bool CopyLatest(BYTE* dst);

private:
    void EnsureOpen();
    HANDLE m_mapping = nullptr;
    BYTE* m_view = nullptr;
};

class MediaStream : public wrl::RuntimeClass<wrl::RuntimeClassFlags<wrl::ClassicCom>,
    wrl::ChainInterfaces<IMFMediaStream2, IMFMediaStream, IMFMediaEventGenerator>>
{
public:
    HRESULT RuntimeClassInitialize(MediaSource* source);

    // IMFMediaEventGenerator
    STDMETHODIMP GetEvent(DWORD flags, IMFMediaEvent** event) override;
    STDMETHODIMP BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state) override;
    STDMETHODIMP EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event) override;
    STDMETHODIMP QueueEvent(MediaEventType type, REFGUID extType, HRESULT status, const PROPVARIANT* eventValue) override;

    // IMFMediaStream
    STDMETHODIMP GetMediaSource(IMFMediaSource** source) override;
    STDMETHODIMP GetStreamDescriptor(IMFStreamDescriptor** descriptor) override;
    STDMETHODIMP RequestSample(IUnknown* token) override;

    // IMFMediaStream2
    STDMETHODIMP SetStreamState(MF_STREAM_STATE state) override;
    STDMETHODIMP GetStreamState(MF_STREAM_STATE* state) override;

    HRESULT Start();
    HRESULT Stop();
    void Shutdown();
    IMFAttributes* Attributes() { return m_attributes.Get(); }
    IMFStreamDescriptor* Descriptor() { return m_descriptor.Get(); }

private:
    HRESULT FillBuffer(BYTE* data, const GUID& subtype);

    std::mutex m_lock;
    MediaSource* m_source = nullptr; // weak, the source owns the stream
    ComPtr<IMFMediaEventQueue> m_queue;
    ComPtr<IMFStreamDescriptor> m_descriptor;
    ComPtr<IMFAttributes> m_attributes;
    MF_STREAM_STATE m_state = MF_STREAM_STATE_STOPPED;
    bool m_shutdown = false;
    SharedFrameReader m_reader;
    std::unique_ptr<BYTE[]> m_bgra;
    bool m_hasFrame = false;
};

class MediaSource : public wrl::RuntimeClass<wrl::RuntimeClassFlags<wrl::ClassicCom>,
    wrl::ChainInterfaces<IMFMediaSourceEx, IMFMediaSource, IMFMediaEventGenerator>, IMFGetService, IKsControl>
{
public:
    HRESULT RuntimeClassInitialize(IMFAttributes* activateAttributes);

    // IMFMediaEventGenerator
    STDMETHODIMP GetEvent(DWORD flags, IMFMediaEvent** event) override;
    STDMETHODIMP BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state) override;
    STDMETHODIMP EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event) override;
    STDMETHODIMP QueueEvent(MediaEventType type, REFGUID extType, HRESULT status, const PROPVARIANT* eventValue) override;

    // IMFMediaSource
    STDMETHODIMP GetCharacteristics(DWORD* characteristics) override;
    STDMETHODIMP CreatePresentationDescriptor(IMFPresentationDescriptor** descriptor) override;
    STDMETHODIMP Start(IMFPresentationDescriptor* descriptor, const GUID* timeFormat, const PROPVARIANT* startPosition) override;
    STDMETHODIMP Stop() override;
    STDMETHODIMP Pause() override;
    STDMETHODIMP Shutdown() override;

    // IMFMediaSourceEx
    STDMETHODIMP GetSourceAttributes(IMFAttributes** attributes) override;
    STDMETHODIMP GetStreamAttributes(DWORD streamId, IMFAttributes** attributes) override;
    STDMETHODIMP SetD3DManager(IUnknown* manager) override;

    // IMFGetService
    STDMETHODIMP GetService(REFGUID service, REFIID riid, LPVOID* object) override;

    // IKsControl (required by the Frame Server, no custom properties)
    STDMETHODIMP KsProperty(PKSPROPERTY property, ULONG propertyLength, LPVOID data, ULONG dataLength, ULONG* bytesReturned) override;
    STDMETHODIMP KsMethod(PKSMETHOD method, ULONG methodLength, LPVOID data, ULONG dataLength, ULONG* bytesReturned) override;
    STDMETHODIMP KsEvent(PKSEVENT event, ULONG eventLength, LPVOID data, ULONG dataLength, ULONG* bytesReturned) override;

private:
    std::mutex m_lock;
    ComPtr<IMFMediaEventQueue> m_queue;
    ComPtr<IMFAttributes> m_attributes;
    ComPtr<IMFPresentationDescriptor> m_presentation;
    ComPtr<MediaStream> m_stream;
    bool m_streamAnnounced = false;
    bool m_shutdown = false;
};

class Activate : public wrl::RuntimeClass<wrl::RuntimeClassFlags<wrl::ClassicCom>,
    wrl::ChainInterfaces<IMFActivate, IMFAttributes>>
{
public:
    HRESULT RuntimeClassInitialize();

    IMPLEMENT_IMFATTRIBUTES_FORWARDING

    // IMFActivate
    STDMETHODIMP ActivateObject(REFIID riid, void** object) override;
    STDMETHODIMP ShutdownObject() override;
    STDMETHODIMP DetachObject() override;

private:
    ComPtr<IMFAttributes> m_attributes;
    ComPtr<MediaSource> m_source;
};
