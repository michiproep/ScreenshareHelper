#pragma once
#include <mfobjects.h>

// Forwards all IMFAttributes methods to a member "ComPtr<IMFAttributes> m_attributes".
#define IMPLEMENT_IMFATTRIBUTES_FORWARDING \
    STDMETHODIMP GetItem(REFGUID k, PROPVARIANT* v) override { return m_attributes->GetItem(k, v); } \
    STDMETHODIMP GetItemType(REFGUID k, MF_ATTRIBUTE_TYPE* t) override { return m_attributes->GetItemType(k, t); } \
    STDMETHODIMP CompareItem(REFGUID k, REFPROPVARIANT v, BOOL* r) override { return m_attributes->CompareItem(k, v, r); } \
    STDMETHODIMP Compare(IMFAttributes* a, MF_ATTRIBUTES_MATCH_TYPE t, BOOL* r) override { return m_attributes->Compare(a, t, r); } \
    STDMETHODIMP GetUINT32(REFGUID k, UINT32* v) override { return m_attributes->GetUINT32(k, v); } \
    STDMETHODIMP GetUINT64(REFGUID k, UINT64* v) override { return m_attributes->GetUINT64(k, v); } \
    STDMETHODIMP GetDouble(REFGUID k, double* v) override { return m_attributes->GetDouble(k, v); } \
    STDMETHODIMP GetGUID(REFGUID k, GUID* v) override { return m_attributes->GetGUID(k, v); } \
    STDMETHODIMP GetStringLength(REFGUID k, UINT32* l) override { return m_attributes->GetStringLength(k, l); } \
    STDMETHODIMP GetString(REFGUID k, LPWSTR v, UINT32 s, UINT32* l) override { return m_attributes->GetString(k, v, s, l); } \
    STDMETHODIMP GetAllocatedString(REFGUID k, LPWSTR* v, UINT32* l) override { return m_attributes->GetAllocatedString(k, v, l); } \
    STDMETHODIMP GetBlobSize(REFGUID k, UINT32* s) override { return m_attributes->GetBlobSize(k, s); } \
    STDMETHODIMP GetBlob(REFGUID k, UINT8* b, UINT32 s, UINT32* l) override { return m_attributes->GetBlob(k, b, s, l); } \
    STDMETHODIMP GetAllocatedBlob(REFGUID k, UINT8** b, UINT32* s) override { return m_attributes->GetAllocatedBlob(k, b, s); } \
    STDMETHODIMP GetUnknown(REFGUID k, REFIID i, LPVOID* p) override { return m_attributes->GetUnknown(k, i, p); } \
    STDMETHODIMP SetItem(REFGUID k, REFPROPVARIANT v) override { return m_attributes->SetItem(k, v); } \
    STDMETHODIMP DeleteItem(REFGUID k) override { return m_attributes->DeleteItem(k); } \
    STDMETHODIMP DeleteAllItems() override { return m_attributes->DeleteAllItems(); } \
    STDMETHODIMP SetUINT32(REFGUID k, UINT32 v) override { return m_attributes->SetUINT32(k, v); } \
    STDMETHODIMP SetUINT64(REFGUID k, UINT64 v) override { return m_attributes->SetUINT64(k, v); } \
    STDMETHODIMP SetDouble(REFGUID k, double v) override { return m_attributes->SetDouble(k, v); } \
    STDMETHODIMP SetGUID(REFGUID k, REFGUID v) override { return m_attributes->SetGUID(k, v); } \
    STDMETHODIMP SetString(REFGUID k, LPCWSTR v) override { return m_attributes->SetString(k, v); } \
    STDMETHODIMP SetBlob(REFGUID k, const UINT8* b, UINT32 s) override { return m_attributes->SetBlob(k, b, s); } \
    STDMETHODIMP SetUnknown(REFGUID k, IUnknown* u) override { return m_attributes->SetUnknown(k, u); } \
    STDMETHODIMP LockStore() override { return m_attributes->LockStore(); } \
    STDMETHODIMP UnlockStore() override { return m_attributes->UnlockStore(); } \
    STDMETHODIMP GetCount(UINT32* c) override { return m_attributes->GetCount(c); } \
    STDMETHODIMP GetItemByIndex(UINT32 i, GUID* k, PROPVARIANT* v) override { return m_attributes->GetItemByIndex(i, k, v); } \
    STDMETHODIMP CopyAllItems(IMFAttributes* d) override { return m_attributes->CopyAllItems(d); }
