#include <windows.h>
#include <xamlom.h>
#include <string>
#include <cstdio>
static constexpr CLSID TapId={0x9e2a5b72,0x2be3,0x4ac1,{0x97,0x6f,0x3c,0x95,0x85,0x0b,0xe6,0xf1}};
int wmain(int argc,wchar_t** argv) {
    if(argc!=4) return 2;
    DWORD explorer=wcstoul(argv[1],nullptr,10),owner=wcstoul(argv[3],nullptr,10);
    if(!explorer||!owner) return 2;
    auto initialized=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    if(FAILED(initialized)) return 3;
    HMODULE xaml=LoadLibraryExW(L"Windows.UI.Xaml.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!xaml) return 4;
    auto initialize=reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(GetProcAddress(xaml,"InitializeXamlDiagnosticsEx"));
    if(!initialize) return 5;
    HRESULT hr=E_FAIL;
    for(int i=1;i<=100;i++) {
        std::wstring endpoint=L"VisualDiagConnection"+std::to_wstring(i);
        hr=initialize(endpoint.c_str(),explorer,L"",argv[2],TapId,nullptr);
        if(hr!=HRESULT_FROM_WIN32(ERROR_NOT_FOUND)) {fprintf(stderr,"Endpoint %d result %08X\n",i,static_cast<unsigned>(hr));break;}
    }
    printf("%08X\n",static_cast<unsigned>(hr));fflush(stdout);
    if(FAILED(hr)) return 6;
    HANDLE parent=OpenProcess(SYNCHRONIZE,FALSE,owner);
    if(!parent) return 7;
    while(MsgWaitForMultipleObjects(1,&parent,FALSE,INFINITE,QS_ALLINPUT)==WAIT_OBJECT_0+1) {
        MSG message{};while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) {TranslateMessage(&message);DispatchMessageW(&message);}
    }
    CloseHandle(parent);FreeLibrary(xaml);CoUninitialize();return 0;
}
