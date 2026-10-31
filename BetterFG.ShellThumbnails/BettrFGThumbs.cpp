#include <windows.h>
#include <shlobj.h>
#include <thumbcache.h>
#include <wincodec.h>
#include <wincrypt.h>
#include <new>
#include <string>

#pragma comment(lib, "windowscodecs.lib")
#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "gdi32.lib")

static const CLSID CLSID_BettrFGThumbs = { 0xBC60B799, 0xD087, 0x4C6D, { 0x9C, 0x52, 0x05, 0x79, 0x09, 0xF7, 0x4C, 0xF6 } };
static const ULONG MaxFileBytes = 64u * 1024u * 1024u;
static long g_objects = 0;
static long g_locks = 0;

static bool ReadAll(IStream* stream, std::string& out)
{
    STATSTG st = {};
    if (FAILED(stream->Stat(&st, STATFLAG_NONAME)) || st.cbSize.QuadPart > MaxFileBytes) return false;
    out.resize((size_t)st.cbSize.QuadPart);
    ULONG got = 0;
    return SUCCEEDED(stream->Read(&out[0], (ULONG)out.size(), &got)) && got == out.size();
}

static bool CoverBytes(const std::string& json, std::string& png)
{
    const std::string tag = "\"Image\":\"";
    size_t at = json.rfind(tag);
    if (at == std::string::npos) return false;
    at += tag.size();
    size_t end = json.find('"', at);
    if (end == std::string::npos || end == at) return false;

    DWORD size = 0;
    if (!CryptStringToBinaryA(json.data() + at, (DWORD)(end - at), CRYPT_STRING_BASE64, nullptr, &size, nullptr, nullptr)) return false;
    png.resize(size);
    return CryptStringToBinaryA(json.data() + at, (DWORD)(end - at), CRYPT_STRING_BASE64, (BYTE*)&png[0], &size, nullptr, nullptr) != FALSE;
}

static HRESULT Decode(const std::string& png, UINT cx, HBITMAP* bitmap)
{
    IWICImagingFactory* factory = nullptr;
    IWICStream* stream = nullptr;
    IWICBitmapDecoder* decoder = nullptr;
    IWICBitmapFrameDecode* frame = nullptr;
    IWICBitmapScaler* scaler = nullptr;
    IWICFormatConverter* converter = nullptr;

    HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory));
    if (SUCCEEDED(hr)) hr = factory->CreateStream(&stream);
    if (SUCCEEDED(hr)) hr = stream->InitializeFromMemory((BYTE*)png.data(), (DWORD)png.size());
    if (SUCCEEDED(hr)) hr = factory->CreateDecoderFromStream(stream, nullptr, WICDecodeMetadataCacheOnDemand, &decoder);
    if (SUCCEEDED(hr)) hr = decoder->GetFrame(0, &frame);

    UINT w = 0, h = 0;
    if (SUCCEEDED(hr)) hr = frame->GetSize(&w, &h);
    if (SUCCEEDED(hr) && (w == 0 || h == 0)) hr = E_FAIL;

    UINT tw = w, th = h;
    if (SUCCEEDED(hr) && (w > cx || h > cx))
    {
        if (w >= h) { tw = cx; th = max(1u, (UINT)((UINT64)h * cx / w)); }
        else { th = cx; tw = max(1u, (UINT)((UINT64)w * cx / h)); }
    }
    if (SUCCEEDED(hr)) hr = factory->CreateBitmapScaler(&scaler);
    if (SUCCEEDED(hr)) hr = scaler->Initialize(frame, tw, th, WICBitmapInterpolationModeFant);
    if (SUCCEEDED(hr)) hr = factory->CreateFormatConverter(&converter);
    if (SUCCEEDED(hr)) hr = converter->Initialize(scaler, GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherTypeNone, nullptr, 0.0, WICBitmapPaletteTypeCustom);

    if (SUCCEEDED(hr))
    {
        BITMAPINFO bmi = {};
        bmi.bmiHeader.biSize = sizeof(bmi.bmiHeader);
        bmi.bmiHeader.biWidth = (LONG)tw;
        bmi.bmiHeader.biHeight = -(LONG)th;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;
        void* bits = nullptr;
        HBITMAP dib = CreateDIBSection(nullptr, &bmi, DIB_RGB_COLORS, &bits, nullptr, 0);
        hr = dib ? S_OK : E_OUTOFMEMORY;
        if (SUCCEEDED(hr)) hr = converter->CopyPixels(nullptr, tw * 4, tw * 4 * th, (BYTE*)bits);
        if (SUCCEEDED(hr)) *bitmap = dib;
        else if (dib) DeleteObject(dib);
    }

    if (converter) converter->Release();
    if (scaler) scaler->Release();
    if (frame) frame->Release();
    if (decoder) decoder->Release();
    if (stream) stream->Release();
    if (factory) factory->Release();
    return hr;
}

class Provider : public IInitializeWithStream, public IThumbnailProvider
{
    long _refs = 1;
    IStream* _stream = nullptr;

public:
    Provider() { InterlockedIncrement(&g_objects); }
    virtual ~Provider() { if (_stream) _stream->Release(); InterlockedDecrement(&g_objects); }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IThumbnailProvider) *ppv = static_cast<IThumbnailProvider*>(this);
        else if (riid == IID_IInitializeWithStream) *ppv = static_cast<IInitializeWithStream*>(this);
        else { *ppv = nullptr; return E_NOINTERFACE; }
        AddRef();
        return S_OK;
    }
    IFACEMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&_refs); }
    IFACEMETHODIMP_(ULONG) Release()
    {
        long left = InterlockedDecrement(&_refs);
        if (left == 0) delete this;
        return left;
    }

    IFACEMETHODIMP Initialize(IStream* stream, DWORD)
    {
        if (_stream) return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
        _stream = stream;
        _stream->AddRef();
        return S_OK;
    }

    IFACEMETHODIMP GetThumbnail(UINT cx, HBITMAP* bitmap, WTS_ALPHATYPE* alpha)
    {
        if (!_stream || !bitmap || !alpha) return E_UNEXPECTED;
        std::string json, png;
        if (!ReadAll(_stream, json) || !CoverBytes(json, png)) return E_FAIL;
        *alpha = WTSAT_ARGB;
        return Decode(png, cx, bitmap);
    }
};

class Factory : public IClassFactory
{
public:
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory) { *ppv = this; return S_OK; }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() { return 2; }
    IFACEMETHODIMP_(ULONG) Release() { return 1; }

    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv)
    {
        if (outer) return CLASS_E_NOAGGREGATION;
        Provider* p = new (std::nothrow) Provider();
        if (!p) return E_OUTOFMEMORY;
        HRESULT hr = p->QueryInterface(riid, ppv);
        p->Release();
        return hr;
    }
    IFACEMETHODIMP LockServer(BOOL lock)
    {
        if (lock) InterlockedIncrement(&g_locks); else InterlockedDecrement(&g_locks);
        return S_OK;
    }
};

static Factory g_factory;

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv)
{
    if (clsid != CLSID_BettrFGThumbs) return CLASS_E_CLASSNOTAVAILABLE;
    return g_factory.QueryInterface(riid, ppv);
}

STDAPI DllCanUnloadNow()
{
    return g_objects == 0 && g_locks == 0 ? S_OK : S_FALSE;
}
