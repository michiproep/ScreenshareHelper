// COM server entry points (loaded by the Windows camera service) and the
// VirtualCamera_Start/Stop exports used by ScreenshareHelper.exe.
#include "MediaSource.h"
#include "Shared.h"
#include <mfvirtualcamera.h>
#include <new>
#include <string>

static HMODULE g_module = nullptr;
static volatile LONG g_objects = 0;

class ClassFactory : public IClassFactory
{
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** object) override
    {
        if (!object) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory)
        {
            *object = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        *object = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return 2; }  // static instance
    STDMETHODIMP_(ULONG) Release() override { return 1; }

    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** object) override
    {
        if (!object) return E_POINTER;
        *object = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        ComPtr<Activate> activate;
        HRESULT hr = wrl::MakeAndInitialize<Activate>(&activate);
        if (FAILED(hr)) return hr;
        return activate->QueryInterface(riid, object);
    }

    STDMETHODIMP LockServer(BOOL lock) override
    {
        if (lock) InterlockedIncrement(&g_objects); else InterlockedDecrement(&g_objects);
        return S_OK;
    }
};

static ClassFactory g_factory;

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, LPVOID* object)
{
    if (clsid != CLSID_ScreenshareHelperVirtualCamera)
        return CLASS_E_CLASSNOTAVAILABLE;
    return g_factory.QueryInterface(riid, object);
}

STDAPI DllCanUnloadNow()
{
    // objects don't track a module-wide count; staying loaded is the safe choice
    return S_FALSE;
}

// The camera service runs as LocalService and only sees machine-wide COM registrations,
// so this writes to HKLM and needs admin rights (regsvr32 from an elevated prompt).
static const std::wstring ClsidKey = L"SOFTWARE\\Classes\\CLSID\\" VCAM_CLSID_STRING;

STDAPI DllRegisterServer()
{
    wchar_t path[MAX_PATH];
    if (!GetModuleFileNameW(g_module, path, MAX_PATH))
        return HRESULT_FROM_WIN32(GetLastError());

    HKEY key = nullptr;
    LSTATUS status = RegCreateKeyExW(HKEY_LOCAL_MACHINE, (ClsidKey + L"\\InprocServer32").c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr);
    if (status != ERROR_SUCCESS)
        return HRESULT_FROM_WIN32(status);
    RegSetValueExW(key, nullptr, 0, REG_SZ, (const BYTE*)path, DWORD((wcslen(path) + 1) * sizeof(wchar_t)));
    const wchar_t threading[] = L"Both";
    RegSetValueExW(key, L"ThreadingModel", 0, REG_SZ, (const BYTE*)threading, sizeof(threading));
    RegCloseKey(key);

    const wchar_t name[] = L"ScreenshareHelper Virtual Camera Source";
    RegSetKeyValueW(HKEY_LOCAL_MACHINE, ClsidKey.c_str(), nullptr, REG_SZ, name, sizeof(name));
    return S_OK;
}

STDAPI DllUnregisterServer()
{
    LSTATUS status = RegDeleteTreeW(HKEY_LOCAL_MACHINE, ClsidKey.c_str());
    return (status == ERROR_SUCCESS || status == ERROR_FILE_NOT_FOUND) ? S_OK : HRESULT_FROM_WIN32(status);
}

#pragma region App API

static IMFVirtualCamera* g_camera = nullptr;

extern "C" HRESULT __stdcall VirtualCamera_IsRegistered()
{
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, (ClsidKey + L"\\InprocServer32").c_str(), 0, KEY_READ, &key) != ERROR_SUCCESS)
        return S_FALSE;
    RegCloseKey(key);
    return S_OK;
}

/// Creates a session-lifetime virtual camera for the current user: it exists while this process holds it.
extern "C" HRESULT __stdcall VirtualCamera_Start(LPCWSTR friendlyName)
{
    if (g_camera)
        return S_OK;

    HRESULT hr = MFStartup(MF_VERSION);
    if (FAILED(hr)) return hr;

    hr = MFCreateVirtualCamera(MFVirtualCameraType_SoftwareCameraSource, MFVirtualCameraLifetime_Session,
        MFVirtualCameraAccess_CurrentUser, friendlyName, VCAM_CLSID_STRING, nullptr, 0, &g_camera);
    if (FAILED(hr)) return hr;

    hr = g_camera->Start(nullptr);
    if (FAILED(hr))
    {
        g_camera->Shutdown();
        g_camera->Release();
        g_camera = nullptr;
    }
    return hr;
}

extern "C" HRESULT __stdcall VirtualCamera_Stop()
{
    if (!g_camera)
        return S_OK;
    g_camera->Stop();
    g_camera->Shutdown();
    g_camera->Release();
    g_camera = nullptr;
    MFShutdown();
    return S_OK;
}

#pragma endregion
