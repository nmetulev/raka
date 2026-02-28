// raka_tap.cpp — TAP DLL for Raka WinUI 3 automation
// Injected into the target process by InitializeXamlDiagnosticsEx.
// Implements IObjectWithSite → receives IVisualTreeService → walks XAML tree
// → sends JSON over named pipe back to raka.exe.
//
// Adapted from asklar/lvt (MIT License).
// https://github.com/asklar/lvt

#include <Windows.h>
#include <objbase.h>
#include <ocidl.h>
#include <xamlOM.h>
#include <string>
#include <map>
#include <vector>
#include <cstdio>

// IIDs not provided in any .lib — must define manually
static const IID IID_IVisualTreeServiceCallback =
    { 0xAA7A8931, 0x80E4, 0x4FEC, { 0x8F, 0x3B, 0x55, 0x3F, 0x87, 0xB4, 0x96, 0x6E } };
static const IID IID_IVisualTreeServiceCallback2 =
    { 0xBAD9EB88, 0xAE77, 0x4397, { 0xB9, 0x48, 0x5F, 0xA2, 0xDB, 0x0A, 0x19, 0xEA } };

// Unique CLSID for Raka TAP — must match the CLSID used in InitializeXamlDiagnosticsEx call
// {7A3F1E8D-4B2C-4D6A-9E5F-1C8A2B3D4E5F}
static const CLSID CLSID_RakaTap =
    { 0x7A3F1E8D, 0x4B2C, 0x4D6A, { 0x9E, 0x5F, 0x1C, 0x8A, 0x2B, 0x3D, 0x4E, 0x5F } };

// --- Debug logging ---

static void LogMsg(const char* fmt, ...) {
    static FILE* logFile = nullptr;
    if (!logFile) {
        wchar_t tmp[MAX_PATH];
        GetTempPathW(MAX_PATH, tmp);
        wcscat_s(tmp, L"raka_tap.log");
        logFile = _wfopen(tmp, L"a");
        if (!logFile) return;
    }
    fprintf(logFile, "[tid=%lu] ", GetCurrentThreadId());
    va_list ap;
    va_start(ap, fmt);
    vfprintf(logFile, fmt, ap);
    va_end(ap);
    fprintf(logFile, "\n");
    fflush(logFile);
}

// --- Tree node data ---

struct TreeNode {
    InstanceHandle handle = 0;
    std::wstring type;
    std::wstring name;
    InstanceHandle parent = 0;
    std::vector<InstanceHandle> children;
    std::vector<std::pair<std::wstring, std::wstring>> properties;
    double width = 0, height = 0;
    double offsetX = 0, offsetY = 0;
    bool hasBounds = false;
};

// --- Forward declarations ---

class RakaTap;
static LRESULT CALLBACK TapMsgWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);

// --- The TAP object ---

class RakaTap : public IObjectWithSite, public IVisualTreeServiceCallback2 {
    LONG m_refCount = 1;
    IUnknown* m_site = nullptr;
    IXamlDiagnostics* m_diag = nullptr;
    HWND m_msgWnd = nullptr;
    std::map<InstanceHandle, TreeNode> m_nodes;
    std::vector<InstanceHandle> m_roots;
    std::wstring m_pipeName;
    volatile bool m_walkDone = false;

public:
    IVisualTreeService* m_vts = nullptr;
    static constexpr UINT WM_COLLECT_BOUNDS = WM_USER + 200;
    static constexpr UINT WM_DO_TREE_WALK = WM_USER + 201;

    // IUnknown
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown) {
            *ppv = static_cast<IObjectWithSite*>(this);
        } else if (riid == IID_IObjectWithSite) {
            *ppv = static_cast<IObjectWithSite*>(this);
        } else if (riid == IID_IVisualTreeServiceCallback) {
            *ppv = static_cast<IVisualTreeServiceCallback*>(
                static_cast<IVisualTreeServiceCallback2*>(this));
        } else if (riid == IID_IVisualTreeServiceCallback2) {
            *ppv = static_cast<IVisualTreeServiceCallback2*>(this);
        } else {
            *ppv = nullptr;
            return E_NOINTERFACE;
        }
        AddRef();
        return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&m_refCount); }
    ULONG STDMETHODCALLTYPE Release() override {
        LONG c = InterlockedDecrement(&m_refCount);
        if (c == 0) delete this;
        return c;
    }

    // IObjectWithSite::SetSite — called by XAML runtime on the UI thread
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* pSite) override {
        LogMsg("SetSite called, pSite=%p", pSite);

        if (m_site) { m_site->Release(); m_site = nullptr; }
        if (m_vts) { m_vts->Release(); m_vts = nullptr; }
        if (m_diag) { m_diag->Release(); m_diag = nullptr; }

        m_site = pSite;
        if (m_site) m_site->AddRef();

        if (!pSite) return S_OK;

        __try {
            return SetSiteImpl(pSite);
        } __except(EXCEPTION_EXECUTE_HANDLER) {
            LogMsg("SetSiteImpl crashed, code=0x%08X", GetExceptionCode());
            return E_FAIL;
        }
    }

    HRESULT STDMETHODCALLTYPE GetSite(REFIID riid, void** ppvSite) override {
        if (!ppvSite) return E_POINTER;
        if (!m_site) { *ppvSite = nullptr; return E_FAIL; }
        return m_site->QueryInterface(riid, ppvSite);
    }

    // IVisualTreeServiceCallback
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation,
        VisualElement element, VisualMutationType mutationType) override
    {

        if (mutationType == Add) {
            TreeNode node;
            node.handle = element.Handle;
            node.type = element.Type ? element.Type : L"";
            node.name = element.Name ? element.Name : L"";
            node.parent = relation.Parent;
            m_nodes[element.Handle] = std::move(node);

            if (relation.Parent == 0) {
                m_roots.push_back(element.Handle);
            } else {
                auto it = m_nodes.find(relation.Parent);
                if (it != m_nodes.end()) {
                    it->second.children.push_back(element.Handle);
                }
            }
        } else if (mutationType == Remove) {
            m_nodes.erase(element.Handle);
            m_roots.erase(
                std::remove(m_roots.begin(), m_roots.end(), element.Handle),
                m_roots.end());
        }
        return S_OK;
    }

    // IVisualTreeServiceCallback2
    HRESULT STDMETHODCALLTYPE OnElementStateChanged(
        InstanceHandle, VisualElementState, LPCWSTR) override
    {
        return S_OK;
    }

private:
    HRESULT SetSiteImpl(IUnknown* pSite) {
        HRESULT hr = pSite->QueryInterface(__uuidof(IXamlDiagnostics), (void**)&m_diag);
        if (FAILED(hr) || !m_diag) {
            LogMsg("QI for IXamlDiagnostics failed: 0x%08X", hr);
            return S_OK;
        }

        BSTR initData = nullptr;
        m_diag->GetInitializationData(&initData);
        if (initData) {
            m_pipeName = initData;
            SysFreeString(initData);
        }
        LogMsg("Pipe: %ls", m_pipeName.c_str());

        hr = pSite->QueryInterface(__uuidof(IVisualTreeService), (void**)&m_vts);
        if (FAILED(hr) || !m_vts) {
            LogMsg("QI for IVisualTreeService failed: 0x%08X", hr);
            return S_OK;
        }

        // Defer tree walk — AdviseVisualTreeChange requires message pumping
        CreateMessageWindow();
        if (m_msgWnd) {
            AddRef();
            PostMessage(m_msgWnd, WM_DO_TREE_WALK, 0, (LPARAM)this);
        } else {
            LogMsg("Failed to create message window");
        }
        return S_OK;
    }

public:
    void DoTreeWalk() {
        // AdviseVisualTreeChange dispatches callbacks cross-thread.
        // Call from a worker thread while pumping messages here.
        AddRef();
        m_walkDone = false;
        HANDLE hThread = CreateThread(nullptr, 0, TreeWalkThreadProc, this, 0, nullptr);
        if (hThread) {
            MSG msg;
            while (!m_walkDone) {
                DWORD waitResult = MsgWaitForMultipleObjects(
                    1, &hThread, FALSE, 15000, QS_ALLINPUT);
                if (waitResult == WAIT_OBJECT_0) {
                    break;
                } else if (waitResult == WAIT_OBJECT_0 + 1) {
                    while (PeekMessage(&msg, nullptr, 0, 0, PM_REMOVE)) {
                        TranslateMessage(&msg);
                        DispatchMessage(&msg);
                    }
                } else {
                    LogMsg("Tree walk timed out");
                    break;
                }
            }
            CloseHandle(hThread);
        }

        LogMsg("Walk complete: %zu nodes, %zu roots", m_nodes.size(), m_roots.size());

        if (!m_nodes.empty()) {
            CollectBoundsOnUIThread();
            SerializeAndSend();
        } else {
            SendEmptyResponse();
        }
        Release();
    }

    static DWORD WINAPI TreeWalkThreadProc(LPVOID param) {
        auto* self = reinterpret_cast<RakaTap*>(param);
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);

        HRESULT hr = self->m_vts->AdviseVisualTreeChange(self);
        if (FAILED(hr)) LogMsg("AdviseVisualTreeChange failed: 0x%08X", hr);
        self->m_vts->UnadviseVisualTreeChange(self);
        self->m_walkDone = true;

        CoUninitialize();
        return 0;
    }

    void SendEmptyResponse() {
        if (m_pipeName.empty()) return;
        const char* json = "[]";
        HANDLE pipe = CreateFileW(m_pipeName.c_str(), GENERIC_WRITE, 0,
                                  nullptr, OPEN_EXISTING, 0, nullptr);
        if (pipe != INVALID_HANDLE_VALUE) {
            DWORD written = 0;
            WriteFile(pipe, json, 2, &written, nullptr);
            FlushFileBuffers(pipe);
            CloseHandle(pipe);
            LogMsg("Sent empty response");
        } else {
            LogMsg("Failed to open pipe for empty response: %lu", GetLastError());
        }
    }

    void CreateMessageWindow() {
        WNDCLASSEXW wc = { sizeof(wc) };
        wc.lpfnWndProc = TapMsgWndProc;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"RakaTapMsg";
        RegisterClassExW(&wc);
        m_msgWnd = CreateWindowExW(0, L"RakaTapMsg", nullptr,
            0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, wc.hInstance, nullptr);
    }

public:
    void CollectBoundsOnUIThread() {
        for (auto& [handle, node] : m_nodes) {
            unsigned int sourceCount = 0;
            unsigned int propCount = 0;
            PropertyChainSource* sources = nullptr;
            PropertyChainValue* values = nullptr;

            HRESULT hr = m_vts->GetPropertyValuesChain(
                handle, &sourceCount, &sources, &propCount, &values);

            if (SUCCEEDED(hr) && values) {
                for (unsigned int i = 0; i < propCount; i++) {
                    std::wstring propName = values[i].PropertyName ? values[i].PropertyName : L"";
                    std::wstring propVal = values[i].Value ? values[i].Value : L"";

                    if (propName == L"ActualWidth" || propName == L"Width") {
                        if (propName == L"ActualWidth" || !node.hasBounds) {
                            try { node.width = std::stod(propVal); } catch (...) {}
                        }
                    } else if (propName == L"ActualHeight" || propName == L"Height") {
                        if (propName == L"ActualHeight" || !node.hasBounds) {
                            try { node.height = std::stod(propVal); } catch (...) {}
                        }
                    }

                    if (propName == L"ActualWidth" || propName == L"ActualHeight") {
                        node.hasBounds = true;
                    }

                    // Store key properties
                    if (propName == L"Name" || propName == L"x:Name" ||
                        propName == L"AutomationProperties.AutomationId" ||
                        propName == L"AutomationProperties.Name" ||
                        propName == L"Text" || propName == L"Content" ||
                        propName == L"Header" || propName == L"PlaceholderText" ||
                        propName == L"Visibility" || propName == L"IsEnabled" ||
                        propName == L"Margin" || propName == L"Padding" ||
                        propName == L"HorizontalAlignment" || propName == L"VerticalAlignment" ||
                        propName == L"Background" || propName == L"Foreground" ||
                        propName == L"FontSize" || propName == L"FontWeight" ||
                        propName == L"ActualWidth" || propName == L"ActualHeight" ||
                        propName == L"Opacity")
                    {
                        if (!propVal.empty() && propVal != L"null") {
                            node.properties.emplace_back(propName, propVal);
                        }
                    }
                }
                CoTaskMemFree(sources);
                CoTaskMemFree(values);
            }
        }
    }

    // --- JSON serialization ---

    static std::wstring Escape(const std::wstring& s) {
        std::wstring r;
        r.reserve(s.size() + 8);
        for (wchar_t c : s) {
            if (c == L'"') r += L"\\\"";
            else if (c == L'\\') r += L"\\\\";
            else if (c == L'\n') r += L"\\n";
            else if (c == L'\r') r += L"\\r";
            else if (c == L'\t') r += L"\\t";
            else if (c < 0x20) {
                wchar_t buf[8];
                swprintf_s(buf, L"\\u%04X", (unsigned)c);
                r += buf;
            } else {
                r += c;
            }
        }
        return r;
    }

    std::wstring SerializeNode(InstanceHandle handle) {
        auto it = m_nodes.find(handle);
        if (it == m_nodes.end()) return L"null";
        auto& n = it->second;

        std::wstring j = L"{\"type\":\"" + Escape(n.type) + L"\"";
        j += L",\"handle\":" + std::to_wstring(n.handle);

        if (!n.name.empty())
            j += L",\"name\":\"" + Escape(n.name) + L"\"";

        if (n.hasBounds) {
            char buf[128];
            snprintf(buf, sizeof(buf),
                ",\"width\":%.1f,\"height\":%.1f",
                n.width, n.height);
            for (const char* p = buf; *p; p++) j += static_cast<wchar_t>(*p);
        }

        // Properties
        if (!n.properties.empty()) {
            j += L",\"properties\":{";
            bool first = true;
            for (auto& [pname, pval] : n.properties) {
                if (!first) j += L",";
                j += L"\"" + Escape(pname) + L"\":\"" + Escape(pval) + L"\"";
                first = false;
            }
            j += L"}";
        }

        // Children
        if (!n.children.empty()) {
            j += L",\"children\":[";
            for (size_t i = 0; i < n.children.size(); i++) {
                if (i) j += L",";
                j += SerializeNode(n.children[i]);
            }
            j += L"]";
        }

        j += L"}";
        return j;
    }

    void SerializeAndSend() {
        LogMsg("SerializeAndSend: nodes=%zu, roots=%zu, pipe=%ls",
               m_nodes.size(), m_roots.size(), m_pipeName.c_str());

        if (m_pipeName.empty() || m_nodes.empty()) return;

        std::wstring json = L"[";
        for (size_t i = 0; i < m_roots.size(); i++) {
            if (i) json += L",";
            json += SerializeNode(m_roots[i]);
        }
        json += L"]";

        // Convert to UTF-8
        int len = WideCharToMultiByte(CP_UTF8, 0, json.c_str(), (int)json.size(),
                                      nullptr, 0, nullptr, nullptr);
        std::string utf8(len, '\0');
        WideCharToMultiByte(CP_UTF8, 0, json.c_str(), (int)json.size(),
                            utf8.data(), len, nullptr, nullptr);

        // Write to named pipe
        HANDLE pipe = CreateFileW(m_pipeName.c_str(), GENERIC_WRITE, 0,
                                  nullptr, OPEN_EXISTING, 0, nullptr);
        if (pipe != INVALID_HANDLE_VALUE) {
            DWORD written = 0;
            WriteFile(pipe, utf8.data(), (DWORD)utf8.size(), &written, nullptr);
            FlushFileBuffers(pipe);
            CloseHandle(pipe);
            LogMsg("Wrote %lu bytes to pipe", written);
        } else {
            LogMsg("Failed to open pipe: error %lu", GetLastError());
        }
    }

    ~RakaTap() {
        if (m_msgWnd) DestroyWindow(m_msgWnd);
        if (m_vts) m_vts->Release();
        if (m_diag) m_diag->Release();
        if (m_site) m_site->Release();
    }
};

// Message window proc for UI thread dispatch
static LRESULT CALLBACK TapMsgWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    if (msg == RakaTap::WM_COLLECT_BOUNDS) {
        auto* self = reinterpret_cast<RakaTap*>(lParam);
        if (self) self->CollectBoundsOnUIThread();
        return 0;
    }
    if (msg == RakaTap::WM_DO_TREE_WALK) {
        auto* self = reinterpret_cast<RakaTap*>(lParam);
        if (self) {
            self->DoTreeWalk();
            self->Release(); // Balance AddRef from SetSiteImpl
        }
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

// --- COM Class Factory ---

class RakaTapFactory : public IClassFactory {
    LONG m_refCount = 1;
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory) {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&m_refCount); }
    ULONG STDMETHODCALLTYPE Release() override {
        LONG c = InterlockedDecrement(&m_refCount);
        if (c == 0) delete this;
        return c;
    }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* pOuter, REFIID riid, void** ppv) override {
        if (pOuter) return CLASS_E_NOAGGREGATION;
        auto* tap = new (std::nothrow) RakaTap();
        if (!tap) return E_OUTOFMEMORY;
        HRESULT hr = tap->QueryInterface(riid, ppv);
        tap->Release();
        return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) override { return S_OK; }
};

// --- DLL exports ---

extern "C" {

HRESULT STDAPICALLTYPE DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv) {
    LogMsg("DllGetClassObject called");
    if (rclsid != CLSID_RakaTap) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new (std::nothrow) RakaTapFactory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

HRESULT STDAPICALLTYPE DllCanUnloadNow() { return S_FALSE; }

BOOL APIENTRY DllMain(HMODULE hMod, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hMod);
        LogMsg("DllMain: DLL_PROCESS_ATTACH");
    }
    return TRUE;
}

} // extern "C"
