// The COM diagnostics plumbing follows TaskbarWidgets (MIT; see THIRD-PARTY.md).
#define DllGetClassObject TaskeeSdkDllGetClassObject
#define DllCanUnloadNow TaskeeSdkDllCanUnloadNow
#include <windows.h>
#include <ocidl.h>
#include <xamlom.h>
#undef DllGetClassObject
#undef DllCanUnloadNow
#undef GetCurrentTime
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <cstdio>
#include <algorithm>
#include <cmath>
#include <fstream>
#include <memory>
#include <mutex>
#include <string>
#include <thread>

namespace x = winrt::Windows::UI::Xaml;
namespace c = x::Controls;
namespace f = winrt::Windows::Foundation;
namespace u = winrt::Windows::UI::Core;
static HMODULE module;
static std::mutex logMutex;
static constexpr CLSID TapId = {0x9e2a5b72,0x2be3,0x4ac1,{0x97,0x6f,0x3c,0x95,0x85,0x0b,0xe6,0xf1}};
static void QueueWeatherTest(x::FrameworkElement const& weather);

static std::wstring FilePath(wchar_t const* name) {
    wchar_t path[32768]{};
    GetModuleFileNameW(module, path, ARRAYSIZE(path));
    std::wstring result(path);
    return result.substr(0, result.find_last_of(L"\\/") + 1) + name;
}

static void Log(std::wstring const& message) noexcept {
    std::lock_guard lock(logMutex);
    FILE* file{};
    if (_wfopen_s(&file, FilePath(L"taskee-tap.log").c_str(), L"a, ccs=UTF-8") == 0) {
        SYSTEMTIME time{}; GetLocalTime(&time);
        fwprintf(file, L"%02u:%02u:%02u pid=%lu thread=%lu %s\n", time.wHour, time.wMinute,
                 time.wSecond, GetCurrentProcessId(), GetCurrentThreadId(), message.c_str());
        fclose(file);
    }
}

class Watcher : public winrt::implements<Watcher, IVisualTreeServiceCallback2, winrt::non_agile> {
    winrt::com_ptr<IXamlDiagnostics> diagnostics;
public:
    explicit Watcher(winrt::com_ptr<IUnknown> const& site) : diagnostics(site.as<IXamlDiagnostics>()) {}
    void Start() {
        AddRef();
        std::thread([this] {
            winrt::init_apartment(winrt::apartment_type::multi_threaded);
            HRESULT hr = diagnostics.as<IVisualTreeService3>()->AdviseVisualTreeChange(this);
            Log(L"AdviseVisualTreeChange=" + std::to_wstring(static_cast<unsigned>(hr)));
            Release();
        }).detach();
    }
    void Stop() {
        AddRef();
        std::thread([this] {
            winrt::init_apartment(winrt::apartment_type::multi_threaded);
            auto hr=diagnostics.as<IVisualTreeService3>()->UnadviseVisualTreeChange(this);
            Log(L"UnadviseVisualTreeChange="+std::to_wstring(static_cast<unsigned>(hr)));
            Release();
        }).detach();
    }
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation,
        VisualElement element, VisualMutationType mutation) noexcept override {
        try {
            if (mutation != Add) return S_OK;
            f::IInspectable object;
            winrt::check_hresult(diagnostics->GetIInspectableFromHandle(element.Handle,
                reinterpret_cast<::IInspectable**>(winrt::put_abi(object))));
            auto framework = object.try_as<x::FrameworkElement>();
            std::wstring type(element.Type ? element.Type : L"");
            if (framework && (type == L"Taskbar.TaskbarFrame" || type == L"Taskbar.AugmentedEntryPointButton" ||
                type == L"SystemTray.SystemTrayFrame" || framework.Name()==L"TaskbarFrameRepeater")) {
                Log(L"TREE parent=" + std::to_wstring(relation.Parent) + L" handle=" +
                    std::to_wstring(element.Handle) + L" type=" + type + L" name=" +
                    framework.Name().c_str() + L" width=" + std::to_wstring(framework.ActualWidth()) +
                    L" height=" + std::to_wstring(framework.ActualHeight()));
            }
            if (framework && type == L"Taskbar.AugmentedEntryPointButton") QueueWeatherTest(framework);
        } catch (...) { Log(L"TREE error=" + std::to_wstring(static_cast<unsigned>(winrt::to_hresult()))); }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) noexcept override {
        return S_OK;
    }
};
static winrt::com_ptr<Watcher> watcher;

struct Reservation {
    x::FrameworkElement weather{nullptr};
    x::FrameworkElement repeater{nullptr};
    x::FrameworkElement tray{nullptr};
    x::FrameworkElement frame{nullptr};
    c::Grid grid{nullptr};
    c::Border panel{nullptr};
    x::DispatcherTimer timer{nullptr};
    x::Thickness originalMargin{};
    double expectedSlotRight{};
    double width{320};
    ULONGLONG started{};
    HANDLE stop{}, owner{};
    unsigned tick{};
    bool attached{};
    winrt::event_token tickToken{};
};
static std::shared_ptr<Reservation> reservation;
static bool queued = false;

static x::FrameworkElement FindName(x::DependencyObject const& root, wchar_t const* name) {
    if (auto item = root.try_as<x::FrameworkElement>(); item && item.Name() == name) return item;
    int count = x::Media::VisualTreeHelper::GetChildrenCount(root);
    for (int i=0; i<count; ++i) {
        auto found = FindName(x::Media::VisualTreeHelper::GetChild(root, i), name);
        if (found) return found;
    }
    return nullptr;
}
static x::FrameworkElement FindFrame(x::FrameworkElement const& weather) {
    x::DependencyObject current = weather;
    while (current) {
        if (winrt::get_class_name(current) == L"Taskbar.TaskbarFrame") return current.as<x::FrameworkElement>();
        current = x::Media::VisualTreeHelper::GetParent(current);
    }
    return nullptr;
}
static x::FrameworkElement FindType(x::DependencyObject const& root, wchar_t const* name) {
    if (winrt::get_class_name(root)==name) return root.try_as<x::FrameworkElement>();
    int count=x::Media::VisualTreeHelper::GetChildrenCount(root);
    for (int i=0;i<count;++i) {
        auto found=FindType(x::Media::VisualTreeHelper::GetChild(root,i),name);
        if (found) return found;
    }
    return nullptr;
}
static f::Rect Bounds(x::FrameworkElement const& item, x::FrameworkElement const& frame) {
    return item.TransformToVisual(frame).TransformBounds({0,0,
        static_cast<float>(item.ActualWidth()),static_cast<float>(item.ActualHeight())});
}
static std::wstring Describe(f::Rect const& rect) {
    return L"x="+std::to_wstring(rect.X)+L" w="+std::to_wstring(rect.Width);
}
static void WriteStatus(std::wstring const& message) {
    FILE* file{};
    if (_wfopen_s(&file, FilePath(L"taskee-status.txt").c_str(), L"w, ccs=UTF-8") == 0) {
        fwprintf(file, L"%s\n", message.c_str()); fclose(file);
    }
}
static void Restore(std::shared_ptr<Reservation> const& state) {
    if (!state->attached) return;
    state->timer.Stop();
    state->timer.Tick(state->tickToken);
    uint32_t index{};
    if (state->grid.Children().IndexOf(state->panel, index)) state->grid.Children().RemoveAt(index);
    state->repeater.Margin(state->originalMargin);
    state->attached = false;
    if (state->stop) { CloseHandle(state->stop); state->stop = nullptr; }
    if (state->owner) { CloseHandle(state->owner); state->owner = nullptr; }
    WriteStatus(L"REMOVED: taskbar margin restored and test panel removed.");
    Log(L"RESTORED original taskbar repeater margin and removed panel");
    if (watcher) watcher->Stop();
    state->timer=x::DispatcherTimer();
    state->timer.Interval(std::chrono::milliseconds(1000));
    state->tickToken=state->timer.Tick([state](auto const&,auto const&) {
        try {
            auto margin=state->repeater.Margin();
            bool exact=margin.Left==state->originalMargin.Left && margin.Right==state->originalMargin.Right &&
                margin.Top==state->originalMargin.Top && margin.Bottom==state->originalMargin.Bottom;
            Log(L"RESTORE_CHECK exactMargin="+std::to_wstring(exact)+L" repeaterWidth="+
                std::to_wstring(state->repeater.ActualWidth()));
            WriteStatus(exact ? L"PASS: test removed; exact original taskbar margin restored." : L"FAIL: restore check did not match.");
        } catch (...) { Log(L"Restore verification failed"); }
        state->timer.Stop(); state->timer.Tick(state->tickToken);
        state->weather=nullptr; state->frame=nullptr; state->grid=nullptr;
        state->repeater=nullptr; state->tray=nullptr; state->panel=nullptr; state->timer=nullptr;
    });
    state->timer.Start();
    // Retain the diagnostics module reference until Explorer naturally exits.
    // Unloading it while XAML owns COM objects is unsafe.
}
static void Sample(std::shared_ptr<Reservation> const& state) {
    if (!state->attached) return;
    bool stopping = state->stop && WaitForSingleObject(state->stop, 0) == WAIT_OBJECT_0;
    bool exited = state->owner && WaitForSingleObject(state->owner, 0) == WAIT_OBJECT_0;
    if (stopping || exited || GetTickCount64()-state->started > 180000) { Restore(state); return; }
    auto slot = Bounds(state->weather,state->frame);
    auto stats = Bounds(state->panel,state->frame);
    auto repeater = FindName(state->frame, L"TaskbarFrameRepeater");
    int overlaps{}; int buttons{}; int overflowButtons{}; float nearestRight{};
    if (repeater) {
        auto count = x::Media::VisualTreeHelper::GetChildrenCount(repeater);
        for (int i=0;i<count;++i) {
            auto child = x::Media::VisualTreeHelper::GetChild(repeater,i).try_as<x::FrameworkElement>();
            if (!child || child.Visibility() != x::Visibility::Visible) continue;
            auto type=winrt::get_class_name(child);
            if (type!=L"Taskbar.TaskListButton") {
                if (std::wstring(type).find(L"Overflow")!=std::wstring::npos) ++overflowButtons;
                if (state->tick==16) Log(L"CONTROL type="+std::wstring(type)+L" name="+
                    child.Name().c_str()+L" automation="+x::Automation::AutomationProperties::GetName(child).c_str()+
                    L" "+Describe(Bounds(child,state->frame)));
                continue;
            }
            auto b = Bounds(child,state->frame);
            if (b.Width<=0 || b.X<0 || b.X>=state->frame.ActualWidth()) continue;
            ++buttons; nearestRight=std::max(nearestRight,b.X+b.Width);
            if (b.X<stats.X+stats.Width-1 && b.X+b.Width>stats.X+1) ++overlaps;
        }
    }
    bool weatherOverlap=slot.X<stats.X+stats.Width-1 && slot.X+slot.Width>stats.X+1;
    bool outside=stats.X+stats.Width>state->expectedSlotRight+1;
    bool held=std::abs(state->repeater.Margin().Right-state->originalMargin.Right-state->width-8)<0.1;
    auto tray=Bounds(state->tray,state->frame);
    bool trayOverlap=stats.X+stats.Width>tray.X+1;
    bool good=!overlaps && !weatherOverlap && !outside && held && !trayOverlap;
    state->panel.Opacity(good ? 1.0 : 0.0);
    std::wstring status = L"Native Weather: "+Describe(slot)+L"\nReserved stats panel: "+Describe(stats)+
        L"\nVisible app buttons: "+std::to_wstring(buttons)+L"; overlapping stats: "+std::to_wstring(overlaps)+
        L"\nWeather overlap: "+std::to_wstring(weatherOverlap)+L"; reservation held: "+std::to_wstring(held)+
        L"; tray overlap: "+std::to_wstring(trayOverlap)+L"\nTEST VALUES: temperatures, watts and transfer rates are simulated.";
    WriteStatus(status);
    if (state->tick++%4 == 0) Log(L"SAMPLE slot "+Describe(slot)+L" stats "+Describe(stats)+
        L" buttons="+std::to_wstring(buttons)+L" overlap="+std::to_wstring(overlaps)+
        L" lastAppRight="+std::to_wstring(nearestRight)+L" weatherOverlap="+std::to_wstring(weatherOverlap)+
        L" reservationHeld="+std::to_wstring(held)+L" repeaterWidth="+std::to_wstring(state->repeater.ActualWidth())+
        L" tray "+Describe(tray)+L" trayOverlap="+std::to_wstring(trayOverlap)+L" overflowControls="+std::to_wstring(overflowButtons));
    if (GetTickCount64()-state->started>2500 && !good) {
        Log(L"FAILED reservation validation; restoring immediately");
        Restore(state);
        WriteStatus(L"FAILED: Windows did not honor the reservation. Original taskbar restored.");
    }
}
static void Attach(x::FrameworkElement const& weather) {
    if (reservation && reservation->attached) return;
    auto frame = FindFrame(weather);
    if (!frame || frame.ActualWidth()<900 || frame.ActualHeight()>100) { queued=false; return; }
    RECT taskbar{}; HWND tray=FindWindowW(L"Shell_TrayWnd",nullptr); GetWindowRect(tray,&taskbar);
    double primaryWidth=(taskbar.right-taskbar.left)*96.0/GetDpiForWindow(tray);
    if (std::abs(primaryWidth-frame.ActualWidth())>3) { queued=false; return; }
    auto grid=FindName(frame,L"RootGrid").try_as<c::Grid>();
    auto repeater=FindName(frame,L"TaskbarFrameRepeater");
    auto trayElement=FindType(frame.XamlRoot().Content(),L"SystemTray.SystemTrayFrame");
    if (!grid || !repeater || !trayElement) { Log(L"Unsupported taskbar root; no changes"); queued=false; return; }
    auto state=std::make_shared<Reservation>();
    state->weather=weather; state->frame=frame; state->grid=grid; state->repeater=repeater;
    state->tray=trayElement;
    state->originalMargin=repeater.Margin();
    auto before=Bounds(weather,frame);
    state->expectedSlotRight=before.X+before.Width;
    DWORD ownerPid{};
    std::wifstream config(FilePath(L"taskee-owner.txt")); config>>ownerPid;
    state->owner=OpenProcess(SYNCHRONIZE,FALSE,ownerPid);
    state->stop=OpenEventW(SYNCHRONIZE,FALSE,L"Local\\TaskeeReservationTest.Stop");
    if (!state->owner || !state->stop) {
        if (state->owner) CloseHandle(state->owner);
        if (state->stop) CloseHandle(state->stop);
        Log(L"Controller absent; no changes"); queued=false; return;
    }
    Log(L"BEFORE weather "+Describe(before)+L" repeaterWidth="+std::to_wstring(repeater.ActualWidth())+
        L" marginRight="+std::to_wstring(state->originalMargin.Right));
    c::Border panel; panel.Name(L"TaskeeReservationTest"); panel.Width(state->width); panel.Height(40);
    panel.Padding({12,2,8,2}); panel.BorderThickness({1,0,0,0});
    panel.BorderBrush(x::Media::SolidColorBrush(winrt::Windows::UI::Color{120,130,150,180}));
    panel.IsHitTestVisible(false);
    panel.Opacity(0.0); // Wait for the native layout animation to settle.
    panel.HorizontalAlignment(x::HorizontalAlignment::Left);
    panel.VerticalAlignment(x::VerticalAlignment::Center);
    panel.Margin({state->expectedSlotRight-state->width,0,0,0});
    c::Grid::SetColumnSpan(panel,std::max(1,static_cast<int>(grid.ColumnDefinitions().Size())));
    c::StackPanel stack;
    for (auto line : {L"TEST  CPU 52°C · 38W   ↓ 12.4 MB/s",L"GPU 61°C · 125W       ↑ 820 KB/s"}) {
        c::TextBlock text; text.Text(line); text.FontSize(11); text.TextWrapping(x::TextWrapping::NoWrap);
        text.Foreground(x::Media::SolidColorBrush(winrt::Windows::UI::Color{255,180,220,255}));
        stack.Children().Append(text);
    }
    panel.Child(stack);
    state->panel=panel;
    // Inset the taskbar layout so app buttons and the native Weather control
    // have less available width. The panel occupies that reserved region.
    auto margin=state->originalMargin; margin.Right+=state->width+8;
    repeater.Margin(margin);
    grid.Children().Append(panel);
    state->attached=true; state->started=GetTickCount64(); reservation=state;
    state->timer=x::DispatcherTimer(); state->timer.Interval(std::chrono::milliseconds(500));
    state->tickToken=state->timer.Tick([state](auto const&,auto const&) {
        try { Sample(state); } catch (...) { Log(L"Sample failed; restoring"); try { Restore(state); } catch (...) { Log(L"Restore failed"); } }
    });
    state->timer.Start();
    Log(L"ATTACHED native XAML panel with 328 DIP taskbar layout inset");
}
static void QueueWeatherTest(x::FrameworkElement const& weather) {
    if (queued || (reservation && reservation->attached)) return;
    auto frame=FindFrame(weather);
    if (!frame) return;
    RECT rect{}; HWND tray=FindWindowW(L"Shell_TrayWnd",nullptr); GetWindowRect(tray,&rect);
    if (std::abs((rect.right-rect.left)*96.0/GetDpiForWindow(tray)-frame.ActualWidth())>3) return;
    queued=true;
    weather.Dispatcher().RunAsync(u::CoreDispatcherPriority::Low,[weather] {
        try { Attach(weather); } catch (...) { queued=false; Log(L"Attach failed="+std::to_wstring(static_cast<unsigned>(winrt::to_hresult()))); }
    });
}

class Tap : public winrt::implements<Tap, IObjectWithSite, winrt::non_agile> {
    winrt::com_ptr<IUnknown> site;
public:
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* value) noexcept override {
        try {
            site.copy_from(value);
            Log(L"SetSite");
            if (site && !watcher) { watcher = winrt::make_self<Watcher>(site); watcher->Start(); }
            return S_OK;
        } catch (...) { return winrt::to_hresult(); }
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid, void** result) noexcept override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (!site) return E_FAIL;
        return site.as(iid, result);
    }
};
class Factory : public winrt::implements<Factory, IClassFactory> {
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** result) noexcept override {
        if (outer) return CLASS_E_NOAGGREGATION;
        try { return winrt::make<Tap>().as(iid, result); } catch (...) { return winrt::to_hresult(); }
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) noexcept override { return S_OK; }
};
extern "C" __declspec(dllexport) HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) {
    if (!IsEqualCLSID(clsid, TapId)) return CLASS_E_CLASSNOTAVAILABLE;
    try { return winrt::make<Factory>().as(iid, result); } catch (...) { return winrt::to_hresult(); }
}
extern "C" __declspec(dllexport) HRESULT __stdcall DllCanUnloadNow() { return S_FALSE; }
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { module = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
