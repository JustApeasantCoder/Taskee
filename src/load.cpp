#include <windows.h>
#include <shellapi.h>
#include <shobjidl.h>
#include <propkey.h>
#include <propvarutil.h>
#include <string>
#include <vector>
int wmain(int argc,wchar_t** argv) {
    unsigned seconds=argc>1?wcstoul(argv[1],nullptr,10):15;seconds=seconds<1?1:seconds>60?60:seconds;
    CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    WNDCLASSW klass{};klass.lpfnWndProc=DefWindowProcW;klass.hInstance=GetModuleHandleW(nullptr);klass.lpszClassName=L"TaskeeLayoutLoad";RegisterClassW(&klass);
    std::vector<HWND> windows;
    for(int i=0;i<48;i++) {
        std::wstring title=L"Taskee layout test "+std::to_wstring(i+1);
        HWND window=CreateWindowExW(WS_EX_APPWINDOW|WS_EX_NOACTIVATE,klass.lpszClassName,title.c_str(),WS_OVERLAPPEDWINDOW,100,100,320,120,nullptr,nullptr,klass.hInstance,nullptr);
        IPropertyStore* properties{};if(SUCCEEDED(SHGetPropertyStoreForWindow(window,IID_PPV_ARGS(&properties)))) {PROPVARIANT id{};std::wstring app=L"Taskee.LayoutTest."+std::to_wstring(i);InitPropVariantFromString(app.c_str(),&id);properties->SetValue(PKEY_AppUserModel_ID,id);properties->Commit();PropVariantClear(&id);properties->Release();}
        ShowWindow(window,SW_SHOWMINNOACTIVE);windows.push_back(window);
    }
    ULONGLONG start=GetTickCount64();
    while(GetTickCount64()-start<seconds*1000ULL) {MSG message{};while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) {TranslateMessage(&message);DispatchMessageW(&message);}MsgWaitForMultipleObjects(0,nullptr,FALSE,100,QS_ALLINPUT);}
    for(HWND window:windows) DestroyWindow(window);CoUninitialize();return 0;
}
