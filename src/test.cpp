#include <windows.h>
#include <xamlom.h>
#include <cstdio>
#include <string>
#include <vector>
#include <fstream>
#include <shellapi.h>
#include <shobjidl.h>
#include <propkey.h>
#include <propvarutil.h>
#include <iterator>
static constexpr CLSID TapId = {0x9e2a5b72,0x2be3,0x4ac1,{0x97,0x6f,0x3c,0x95,0x85,0x0b,0xe6,0xf1}};
static HANDLE stop;
static HWND statusText;
static std::wstring directory;
static std::vector<HWND> loadWindows;
static ULONGLONG started;
static bool selfTest{};
static int stage{};
static DWORD attachedExplorer{};
static void ClearLoad() { for (auto window:loadWindows) if (IsWindow(window)) DestroyWindow(window); loadWindows.clear(); }
static void AddLoad(HWND owner) {
    ClearLoad();
    for (int i=0;i<48;++i) {
        std::wstring title=L"Taskee load "+std::to_wstring(i+1);
        HWND window=CreateWindowExW(WS_EX_APPWINDOW|WS_EX_NOACTIVATE,L"TaskeeLoadWindow",title.c_str(),
            WS_OVERLAPPEDWINDOW,100,100,320,140,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        IPropertyStore* properties{};
        if (SUCCEEDED(SHGetPropertyStoreForWindow(window,IID_PPV_ARGS(&properties)))) {
            PROPVARIANT id{}; std::wstring appId=L"Taskee.ReservationTest.Load."+std::to_wstring(i);
            InitPropVariantFromString(appId.c_str(),&id); properties->SetValue(PKEY_AppUserModel_ID,id);
            properties->Commit(); PropVariantClear(&id); properties->Release();
        }
        ShowWindow(window,SW_SHOWMINNOACTIVE); loadWindows.push_back(window);
    }
    SetWindowTextW(owner,L"Taskee reservation test — 48 temporary app buttons");
}
static LRESULT CALLBACK Controller(HWND window,UINT message,WPARAM wParam,LPARAM lParam) {
    if (message==WM_COMMAND) {
        if (LOWORD(wParam)==1) AddLoad(window);
        if (LOWORD(wParam)==2) { ClearLoad(); SetWindowTextW(window,L"Taskee reservation test"); }
        if (LOWORD(wParam)==3) DestroyWindow(window);
        return 0;
    }
    if (message==WM_TIMER) {
        DWORD currentExplorer{}; GetWindowThreadProcessId(FindWindowW(L"Shell_TrayWnd",nullptr),&currentExplorer);
        if (currentExplorer!=attachedExplorer) { wprintf(L"FAILED: Explorer changed; stopping test\n"); fflush(stdout); DestroyWindow(window); return 0; }
        auto elapsed=GetTickCount64()-started;
        if (selfTest && stage==0 && elapsed>6000) { AddLoad(window); stage=1; wprintf(L"SELFTEST added 48 temporary windows\n"); fflush(stdout); }
        if (selfTest && stage==1 && elapsed>23000) { ClearLoad(); stage=2; wprintf(L"SELFTEST removed all temporary windows\n"); fflush(stdout); }
        std::ifstream file(directory+L"taskee-status.txt",std::ios::binary);
        std::string bytes((std::istreambuf_iterator<char>(file)),std::istreambuf_iterator<char>());
        if (bytes.starts_with("\xEF\xBB\xBF")) bytes.erase(0,3);
        int length=MultiByteToWideChar(CP_UTF8,0,bytes.data(),static_cast<int>(bytes.size()),nullptr,0);
        std::wstring text(length,L'\0');
        if (length) MultiByteToWideChar(CP_UTF8,0,bytes.data(),static_cast<int>(bytes.size()),text.data(),length);
        if (text.empty()) text=L"Waiting for the native Weather slot...";
        text+=L"\r\nTest removes itself after 3 minutes or when this window closes.";
        SetWindowTextW(statusText,text.c_str());
        if (elapsed>175000 || (selfTest && elapsed>30000) || WaitForSingleObject(stop,0)==WAIT_OBJECT_0) DestroyWindow(window);
        return 0;
    }
    if (message==WM_DESTROY) { SetEvent(stop); ClearLoad(); PostQuitMessage(0); return 0; }
    return DefWindowProcW(window,message,wParam,lParam);
}
int wmain(int argc,wchar_t** argv) {
    if (argc>1 && std::wstring(argv[1])==L"--stop") {
        HANDLE event=OpenEventW(EVENT_MODIFY_STATE,FALSE,L"Local\\TaskeeReservationTest.Stop");
        if (event) { SetEvent(event); CloseHandle(event); } return 0;
    }
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    selfTest=argc>1 && std::wstring(argv[1])==L"--self-test";
    CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    HWND tray = FindWindowW(L"Shell_TrayWnd", nullptr);
    DWORD pid{}; GetWindowThreadProcessId(tray, &pid);
    attachedExplorer=pid;
    if (!pid) { fwprintf(stderr, L"Taskbar unavailable\n"); return 1; }
    wchar_t path[32768]{}; GetModuleFileNameW(nullptr, path, ARRAYSIZE(path));
    directory=std::wstring(path).substr(0,std::wstring(path).find_last_of(L"\\/")+1);
    std::wstring dll=directory+L"TaskeeTap.dll";
    stop=CreateEventW(nullptr,TRUE,FALSE,L"Local\\TaskeeReservationTest.Stop");
    if (GetLastError()==ERROR_ALREADY_EXISTS) { fwprintf(stderr,L"Another test is active. Close it first.\n"); return 5; }
    { std::wofstream owner(directory+L"taskee-owner.txt"); owner<<GetCurrentProcessId(); }
    HINSTANCE instance=GetModuleHandleW(nullptr);
    WNDCLASSW controlClass{}; controlClass.lpfnWndProc=Controller; controlClass.hInstance=instance;
    controlClass.lpszClassName=L"TaskeeTestController"; controlClass.hCursor=LoadCursorW(nullptr,IDC_ARROW);
    controlClass.hbrBackground=reinterpret_cast<HBRUSH>(COLOR_WINDOW+1); RegisterClassW(&controlClass);
    WNDCLASSW loadClass=controlClass; loadClass.lpfnWndProc=DefWindowProcW; loadClass.lpszClassName=L"TaskeeLoadWindow";
    RegisterClassW(&loadClass);
    HWND control=CreateWindowExW(WS_EX_APPWINDOW,L"TaskeeTestController",L"Taskee reservation test",
        WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX,CW_USEDEFAULT,CW_USEDEFAULT,640,255,
        nullptr,nullptr,instance,nullptr);
    statusText=CreateWindowW(L"STATIC",L"Attaching to native Weather...",WS_CHILD|WS_VISIBLE,
        18,18,590,130,control,nullptr,instance,nullptr);
    CreateWindowW(L"BUTTON",L"Add 48 test app buttons",WS_CHILD|WS_VISIBLE,18,164,215,32,control,reinterpret_cast<HMENU>(1),instance,nullptr);
    CreateWindowW(L"BUTTON",L"Clear test buttons",WS_CHILD|WS_VISIBLE,245,164,175,32,control,reinterpret_cast<HMENU>(2),instance,nullptr);
    CreateWindowW(L"BUTTON",L"Remove test",WS_CHILD|WS_VISIBLE,432,164,150,32,control,reinterpret_cast<HMENU>(3),instance,nullptr);
    HMODULE xaml = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!xaml) { fwprintf(stderr, L"Windows XAML unavailable: %lu\n", GetLastError()); return 2; }
    auto initialize = reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(GetProcAddress(xaml, "InitializeXamlDiagnosticsEx"));
    if (!initialize) return 3;
    HRESULT hr = E_FAIL;
    for (int i=1; i<=100; ++i) {
        std::wstring name = L"VisualDiagConnection"+std::to_wstring(i);
        hr = initialize(name.c_str(), pid, L"", dll.c_str(), TapId, nullptr);
        if (hr != HRESULT_FROM_WIN32(ERROR_NOT_FOUND)) break;
    }
    wprintf(L"Taskbar PID=%lu; diagnostics attachment=0x%08X\n", pid, static_cast<unsigned>(hr));
    fflush(stdout);
    FreeLibrary(xaml);
    if (FAILED(hr)) { DestroyWindow(control); CloseHandle(stop); return 4; }
    started=GetTickCount64(); SetTimer(control,1,500,nullptr); ShowWindow(control,SW_SHOWNOACTIVATE);
    ShowWindow(control,SW_SHOWNOACTIVATE);
    MSG message{}; while (GetMessageW(&message,nullptr,0,0)>0) { TranslateMessage(&message); DispatchMessageW(&message); }
    CloseHandle(stop); CoUninitialize(); return 0;
}
