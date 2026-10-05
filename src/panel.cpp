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
#include <winrt/Windows.UI.Xaml.Input.h>
#include <winrt/Windows.Data.Json.h>
#include <cstdio>
#include <algorithm>
#include <cmath>
#include <fstream>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include "layout_policy.h"

namespace x = winrt::Windows::UI::Xaml;
namespace c = x::Controls;
namespace f = winrt::Windows::Foundation;
namespace u = winrt::Windows::UI::Core;
static HMODULE module;
static std::mutex logMutex;
static constexpr CLSID TapId = {0x9e2a5b72,0x2be3,0x4ac1,{0x97,0x6f,0x3c,0x95,0x85,0x0b,0xe6,0xf1}};
static x::FrameworkElement FindFrame(x::FrameworkElement const& weather);
static x::FrameworkElement FindType(x::DependencyObject const& root,wchar_t const* name);
static void QueueWeatherTest(x::FrameworkElement const& weather,InstanceHandle weatherHandle,InstanceHandle frameHandle);
static void QueueTaskbarRemoval(InstanceHandle handle);

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
    InstanceHandle HandleOf(x::FrameworkElement const& item) {
        InstanceHandle handle{};
        winrt::check_hresult(diagnostics->GetHandleFromIInspectable(
            reinterpret_cast<::IInspectable*>(winrt::get_abi(item)),&handle));
        return handle;
    }
    void Start() {
        AddRef();
        std::thread([this] {
            try {
            winrt::init_apartment(winrt::apartment_type::multi_threaded);
            HRESULT hr = diagnostics.as<IVisualTreeService3>()->AdviseVisualTreeChange(this);
            Log(L"AdviseVisualTreeChange=" + std::to_wstring(static_cast<unsigned>(hr)));
            winrt::uninit_apartment();
            } catch (...) {Log(L"Diagnostics subscription failed="+std::to_wstring(static_cast<unsigned>(winrt::to_hresult())));}
            Release();
        }).detach();
    }
    void Stop() {
        AddRef();
        std::thread([this] {
            try {
            winrt::init_apartment(winrt::apartment_type::multi_threaded);
            auto hr=diagnostics.as<IVisualTreeService3>()->UnadviseVisualTreeChange(this);
            Log(L"UnadviseVisualTreeChange="+std::to_wstring(static_cast<unsigned>(hr)));
            winrt::uninit_apartment();
            } catch (...) {Log(L"Diagnostics unsubscribe failed="+std::to_wstring(static_cast<unsigned>(winrt::to_hresult())));}
            Release();
        }).detach();
    }
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation,
        VisualElement element, VisualMutationType mutation) noexcept override {
        try {
            if(mutation==Remove) {QueueTaskbarRemoval(element.Handle);return S_OK;}
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
            if (framework && (type==L"Taskbar.AugmentedEntryPointButton" || type==L"Taskbar.TaskbarFrame" ||
                type==L"SystemTray.SystemTrayFrame" || framework.Name()==L"TaskbarFrameRepeater" || framework.Name()==L"RootGrid")) {
                auto weather=type==L"Taskbar.AugmentedEntryPointButton"?framework:
                    framework.XamlRoot()?FindType(framework.XamlRoot().Content(),L"Taskbar.AugmentedEntryPointButton"):nullptr;
                if(weather) {auto frame=FindFrame(weather);if(frame) QueueWeatherTest(weather,HandleOf(weather),HandleOf(frame));}
            }
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
    bool failed{}, stopped{};
    ULONGLONG lastSnapshot{};
    taskee::LayoutValidation validation;
    taskee::BindingTicket binding{};
    std::vector<InstanceHandle> controls;
    ULONGLONG stableSince{};
    double anchorCandidate{};
    double trayGap{12};
    std::wstring layout;
    std::vector<c::TextBlock> texts;
    std::vector<c::StackPanel> columns;
    double opacity{1};
    winrt::event_token tickToken{};
};
static std::shared_ptr<Reservation> reservation;
static std::mutex bindingMutex;
static taskee::TaskbarBinding binding;
namespace j=winrt::Windows::Data::Json;

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
    state->panel.Opacity(0);state->panel.IsHitTestVisible(false);
    try {
        uint32_t index{};
        if (state->grid.Children().IndexOf(state->panel, index)) state->grid.Children().RemoveAt(index);
    } catch(...) {Log(L"Old panel could not be removed; restoring the repeater independently");}
    state->repeater.Margin(state->originalMargin);
    state->attached = false;
    state->stableSince=0;
    state->validation.Reset();
    auto margin=state->repeater.Margin();
    bool exact=margin.Left==state->originalMargin.Left && margin.Right==state->originalMargin.Right && margin.Top==state->originalMargin.Top && margin.Bottom==state->originalMargin.Bottom;
    Log(L"RESTORED exactMargin="+std::to_wstring(exact));
    WriteStatus(L"Taskbar display is off · original spacing restored");
}
static void Detach(std::shared_ptr<Reservation> const& state) {
    if(!state || state->stopped) return;
    state->stopped=true;
    // Removed elements may no longer accept XAML operations. Always release
    // the timer and handles even when restoring their obsolete tree fails.
    try {Restore(state);} catch(...) {Log(L"Obsolete taskbar tree could not be restored");}
    if(state->timer) {try {state->timer.Stop();state->timer.Tick(state->tickToken);} catch(...) { }}
    if(state->owner) {CloseHandle(state->owner);state->owner=nullptr;}
    if(state->stop) {CloseHandle(state->stop);state->stop=nullptr;}
    state->texts.clear();state->columns.clear();state->panel=nullptr;state->weather=nullptr;
    state->repeater=nullptr;state->tray=nullptr;state->frame=nullptr;state->grid=nullptr;state->timer=nullptr;
}
static x::Media::SolidColorBrush Brush(winrt::hstring const& hex) {
    std::wstring text(hex); if(text.size()!=7 && text.size()!=9) text=L"#D8E3EF";
    auto color=static_cast<unsigned long>(std::stoul(text.substr(1),nullptr,16));
    return x::Media::SolidColorBrush(winrt::Windows::UI::Color{
        static_cast<uint8_t>(text.size()==9 ? color>>24 : 255),static_cast<uint8_t>(color>>16),static_cast<uint8_t>(color>>8),static_cast<uint8_t>(color)});
}
static j::JsonObject ReadPanel() {
    HANDLE file=CreateFileW(FilePath(L"panel.json").c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE) return nullptr;
    LARGE_INTEGER size{};
    if(!GetFileSizeEx(file,&size) || size.QuadPart<=0 || size.QuadPart>262144) {CloseHandle(file);return nullptr;}
    std::string bytes(static_cast<size_t>(size.QuadPart),'\0');DWORD read{};
    bool success=ReadFile(file,bytes.data(),static_cast<DWORD>(bytes.size()),&read,nullptr)!=FALSE;CloseHandle(file);
    if(!success||read!=bytes.size()) return nullptr;
    j::JsonObject result{nullptr};
    if(!j::JsonObject::TryParse(winrt::to_hstring(bytes),result) || result.GetNamedNumber(L"version",0)!=1) return nullptr;
    return result;
}
static double Bounded(j::JsonObject const& object,wchar_t const* name,double fallback,double min,double max) {
    double value=object.GetNamedNumber(name,fallback);return std::isfinite(value)?std::clamp(value,min,max):fallback;
}
static void UpdatePanel(std::shared_ptr<Reservation> const& state,j::JsonObject const& snapshot) {
    auto appearance=snapshot.GetNamedObject(L"appearance");
    auto columns=snapshot.GetNamedArray(L"columns");
    if(columns.Size()>32) throw winrt::hresult_invalid_argument();
    std::wstring layout(snapshot.GetNamedString(L"layoutKey"));
    bool rebuild=layout!=state->layout;
    auto panel=state->panel;
    double padding=Bounded(appearance,L"padding",10,0,24),gap=Bounded(appearance,L"columnGap",18,0,48),rowGap=Bounded(appearance,L"rowGap",1,0,8);
    bool separators=appearance.GetNamedBoolean(L"separators",false), fixed=appearance.GetNamedBoolean(L"fixedWidths",true), tooltips=appearance.GetNamedBoolean(L"tooltips",true);
    state->opacity=Bounded(appearance,L"opacity",1,.25,1);
    if(rebuild) {
        state->texts.clear();state->columns.clear();
        c::StackPanel horizontal;horizontal.Orientation(c::Orientation::Horizontal);
        panel.Padding({padding,1,padding,1});panel.Background(Brush(appearance.GetNamedString(L"background",L"#00000000")));
        panel.CornerRadius({Bounded(appearance,L"radius",5,0,16)});
        for(uint32_t i=0;i<columns.Size();i++) {
            auto entries=columns.GetObjectAt(i).GetNamedArray(L"items");
            if(entries.Size()>3) throw winrt::hresult_invalid_argument();
            if(i>0 && separators) { c::Border separator;separator.Width(1);separator.Height(30);separator.Background(Brush(L"#506077"));separator.Opacity(.4);separator.Margin({gap/2,0,gap/2,0});horizontal.Children().Append(separator); }
            c::StackPanel rows;rows.VerticalAlignment(x::VerticalAlignment::Center);rows.Margin({i>0&&!separators?gap:0,0,0,0});
            for(uint32_t k=0;k<entries.Size();k++) {
                c::TextBlock text;text.FontFamily(x::Media::FontFamily(appearance.GetNamedString(L"font",L"Segoe UI")));
                double fit=(std::min(44.0,state->frame.ActualHeight()-4)-2-(entries.Size()-1)*rowGap)/std::max(1u,entries.Size())/1.35;
                text.FontSize(std::min(Bounded(appearance,L"fontSize",11,8,20),fit));
                auto weight=appearance.GetNamedString(L"weight",L"Medium");uint16_t numeric=weight==L"Bold"?700:weight==L"SemiBold"?600:weight==L"Normal"?400:500;
                text.FontWeight(winrt::Windows::UI::Text::FontWeight{numeric});text.TextWrapping(x::TextWrapping::NoWrap);text.Margin({0,0,0,k+1<entries.Size()?rowGap:0});
                rows.Children().Append(text);state->texts.push_back(text);
            }
            horizontal.Children().Append(rows);state->columns.push_back(rows);
        }
        panel.Child(horizontal);state->layout=layout;
    }
    uint32_t n=0;
    for(uint32_t i=0;i<columns.Size();i++) {
        auto entries=columns.GetObjectAt(i).GetNamedArray(L"items");double widest=0;
        for(uint32_t k=0;k<entries.Size();k++) {
            auto item=entries.GetObjectAt(k);auto text=state->texts.at(n++);auto label=item.GetNamedString(L"label",L"");
            std::wstring content=std::wstring(label)+(label.empty()?L"":L"  ")+std::wstring(item.GetNamedString(L"value",L"—"));
            text.Text(content);text.Foreground(Brush(item.GetNamedString(L"color",L"#D8E3EF")));
            if(tooltips) {
                auto tip=c::ToolTipService::GetToolTip(text).try_as<c::ToolTip>();auto tooltipContent=item.GetNamedString(L"tooltip",L"");
                if(!tip) {tip=c::ToolTip();tip.Content(winrt::box_value(tooltipContent));c::ToolTipService::SetToolTip(text,tip);}
                else if(winrt::unbox_value_or<winrt::hstring>(tip.Content(),L"")!=tooltipContent) tip.Content(winrt::box_value(tooltipContent));
            }
            else c::ToolTipService::SetToolTip(text,nullptr);
            text.Measure({10000,100});widest=std::max(widest,static_cast<double>(text.DesiredSize().Width));
            if(fixed) {
                auto actual=text.Text();text.Text(std::wstring(label)+(label.empty()?L"":L"  ")+std::wstring(item.GetNamedString(L"widthHint",L"999.9 MB/s")));text.Measure({10000,100});widest=std::max(widest,static_cast<double>(text.DesiredSize().Width));text.Text(actual);
            }
        }
        if(fixed && !rebuild && std::isfinite(state->columns[i].Width())) widest=std::max(widest,state->columns[i].Width());
        state->columns[i].Width(std::ceil(widest));
    }
    panel.Height(std::min(44.0,state->frame.ActualHeight()-4));
    panel.Child().as<x::FrameworkElement>().Measure({10000,44});
    double wanted=panel.Child().as<x::FrameworkElement>().DesiredSize().Width+padding*2;
    double minimum=Bounded(appearance,L"minimumWidth",0,0,600),maximum=Bounded(appearance,L"maximumWidth",620,std::max(100.0,minimum),1200);
    double width=taskee::PanelWidth(wanted,minimum,maximum,state->expectedSlotRight);
    if(width<=0) {Restore(state);WriteStatus(L"Waiting for enough taskbar space");return;}
    if(rebuild||std::abs(width-state->width)>1 || !state->attached || std::abs(panel.Margin().Left-(state->expectedSlotRight-width))>.5) {
        state->width=width;panel.Width(width);panel.Margin({state->expectedSlotRight-width,0,0,0});
        x::Media::RectangleGeometry clip;clip.Rect({0,0,static_cast<float>(width),static_cast<float>(panel.Height())});panel.Clip(clip);
        auto margin=state->originalMargin;margin.Right+=width+8;state->repeater.Margin(margin);
        if(!state->attached) {state->grid.Children().Append(panel);state->attached=true;}
        panel.Opacity(0);
        Log(L"LAYOUT width="+std::to_wstring(width)+L" columns="+std::to_wstring(columns.Size()));
    }
}
static void Sample(std::shared_ptr<Reservation> const& state) {
    if(state->stopped) return;
    bool stopping = state->stop && WaitForSingleObject(state->stop, 0) == WAIT_OBJECT_0;
    bool exited = state->owner && WaitForSingleObject(state->owner, 0) == WAIT_OBJECT_0;
    if(stopping || exited) {
        {std::lock_guard lock(bindingMutex);binding.Stop();}
        Detach(state);
        if(watcher) watcher->Stop();
        return;
    }
    auto snapshot=ReadPanel();
    if(!snapshot) { if(state->lastSnapshot&&GetTickCount64()-state->lastSnapshot>5000) {Restore(state);WriteStatus(L"Waiting for fresh app data · spacing restored");}return; }
    auto timestamp=static_cast<ULONGLONG>(snapshot.GetNamedNumber(L"timestamp",0));
    FILETIME ft{};GetSystemTimeAsFileTime(&ft);ULARGE_INTEGER value;value.LowPart=ft.dwLowDateTime;value.HighPart=ft.dwHighDateTime;
    ULONGLONG unixMs=value.QuadPart/10000-11644473600000ULL;
    if(unixMs>timestamp+5000) {Restore(state);WriteStatus(L"App data is stale · spacing restored");return;}
    state->lastSnapshot=GetTickCount64();
    if(!snapshot.GetNamedBoolean(L"enabled",false) || snapshot.GetNamedArray(L"columns").Size()==0) {Restore(state);state->failed=false;WriteStatus(L"Taskbar display is off · original spacing restored");return;}
    if(state->failed) return;
    if(!state->attached) {
        auto weather=Bounds(state->weather,state->frame);double anchor=weather.X+weather.Width;
        if(!state->stableSince || std::abs(anchor-state->anchorCandidate)>.5) {state->anchorCandidate=anchor;state->stableSince=GetTickCount64();}
        if(GetTickCount64()-state->stableSince<1000) {WriteStatus(L"Waiting for taskbar layout to settle");return;}
        state->expectedSlotRight=anchor;
        state->trayGap=std::clamp(static_cast<double>(Bounds(state->tray,state->frame).X)-anchor,0.0,60.0);
    } else {
        // The tray grows/shrinks as status icons appear. Follow its edge instead
        // of retaining a screen coordinate captured at startup.
        state->expectedSlotRight=Bounds(state->tray,state->frame).X-state->trayGap;
    }
    UpdatePanel(state,snapshot);
    if(!state->attached) return;
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
            if (type!=L"Taskbar.TaskListButton" && type!=L"Taskbar.OverflowToggleButton") {
                if (std::wstring(type).find(L"Overflow")!=std::wstring::npos) ++overflowButtons;
                if (state->tick==16) Log(L"CONTROL type="+std::wstring(type)+L" name="+
                    child.Name().c_str()+L" automation="+x::Automation::AutomationProperties::GetName(child).c_str()+
                    L" "+Describe(Bounds(child,state->frame)));
                continue;
            }
            auto b = Bounds(child,state->frame);
            if (b.Width<=0 || b.X<0 || b.X>=state->frame.ActualWidth()) continue;
            if(type==L"Taskbar.OverflowToggleButton") ++overflowButtons;else ++buttons;
            nearestRight=std::max(nearestRight,b.X+b.Width);
            if (b.X<stats.X+stats.Width-1 && b.X+b.Width>stats.X+1) ++overlaps;
        }
    }
    bool weatherOverlap=slot.X<stats.X+stats.Width-1 && slot.X+slot.Width>stats.X+1;
    bool outside=stats.X<0 || stats.X+stats.Width>state->expectedSlotRight+1;
    bool held=std::abs(state->repeater.Margin().Right-state->originalMargin.Right-state->width-8)<0.1;
    auto tray=Bounds(state->tray,state->frame);
    bool trayOverlap=stats.X+stats.Width>tray.X+1;
    bool good=!overlaps && !weatherOverlap && !outside && held && !trayOverlap;
    state->panel.Opacity(good ? state->opacity : 0.0);
    state->panel.IsHitTestVisible(good);
    std::wstring status=good?L"Connected · native taskbar space reserved":L"Waiting for taskbar layout";
    status+=L"\nWidth="+std::to_wstring(state->width)+L" DIP; overlaps="+std::to_wstring(overlaps)+L"; weatherOverlap="+std::to_wstring(weatherOverlap)+L"; trayOverlap="+std::to_wstring(trayOverlap);
    WriteStatus(status);
    if (state->tick++%40 == 0) Log(L"SAMPLE slot "+Describe(slot)+L" stats "+Describe(stats)+
        L" buttons="+std::to_wstring(buttons)+L" overlap="+std::to_wstring(overlaps)+
        L" lastAppRight="+std::to_wstring(nearestRight)+L" weatherOverlap="+std::to_wstring(weatherOverlap)+
        L" reservationHeld="+std::to_wstring(held)+L" repeaterWidth="+std::to_wstring(state->repeater.ActualWidth())+
        L" tray "+Describe(tray)+L" trayOverlap="+std::to_wstring(trayOverlap)+L" overflowControls="+std::to_wstring(overflowButtons));
    if (state->validation.Observe(good,GetTickCount64())) {
        Log(L"VALIDATION_FAIL slot "+Describe(slot)+L" stats "+Describe(stats)+L" expectedRight="+std::to_wstring(state->expectedSlotRight)+L" appOverlaps="+std::to_wstring(overlaps)+L" weatherOverlap="+std::to_wstring(weatherOverlap)+L" outside="+std::to_wstring(outside)+L" held="+std::to_wstring(held)+L" trayOverlap="+std::to_wstring(trayOverlap)+L" margin="+std::to_wstring(state->repeater.Margin().Right));
        Log(L"FAILED reservation validation; restoring immediately");
        Restore(state);
        state->failed=true;WriteStatus(L"Windows did not honor the reservation · spacing restored. Toggle the display off and on to retry.");
    }
}
static bool Attach(x::FrameworkElement const& weather,taskee::BindingTicket ticket) {
    auto frame = FindFrame(weather);
    if (!frame || frame.ActualWidth()<900 || frame.ActualHeight()>100) return false;
    RECT taskbar{}; HWND tray=FindWindowW(L"Shell_TrayWnd",nullptr); GetWindowRect(tray,&taskbar);
    double primaryWidth=(taskbar.right-taskbar.left)*96.0/GetDpiForWindow(tray);
    if (std::abs(primaryWidth-frame.ActualWidth())>3) return false;
    auto grid=FindName(frame,L"RootGrid").try_as<c::Grid>();
    auto repeater=FindName(frame,L"TaskbarFrameRepeater");
    auto trayElement=FindType(frame.XamlRoot().Content(),L"SystemTray.SystemTrayFrame");
    if (!grid || !repeater || !trayElement) { Log(L"Unsupported taskbar root; no changes");return false; }
    auto state=std::make_shared<Reservation>();
    struct AttachmentGuard {
        std::shared_ptr<Reservation> state;
        bool committed{};
        ~AttachmentGuard() {if(!committed) Detach(state);}
    } guard{state};
    state->binding=ticket;
    state->weather=weather; state->frame=frame; state->grid=grid; state->repeater=repeater;
    state->tray=trayElement;
    state->controls={watcher->HandleOf(grid),watcher->HandleOf(repeater),watcher->HandleOf(trayElement)};
    state->originalMargin=repeater.Margin();
    auto before=Bounds(weather,frame);
    state->expectedSlotRight=before.X+before.Width;
    DWORD ownerPid{};
    std::wifstream config(FilePath(L"taskee-owner.txt")); config>>ownerPid;
    state->owner=OpenProcess(SYNCHRONIZE,FALSE,ownerPid);
    std::wstring eventName;{std::wifstream file(FilePath(L"taskee-event.txt"));std::getline(file,eventName);}
    state->stop=OpenEventW(SYNCHRONIZE,FALSE,eventName.c_str());
    if (!state->owner || !state->stop) {
        Log(L"Controller absent; no changes");return false;
    }
    Log(L"BEFORE weather "+Describe(before)+L" repeaterWidth="+std::to_wstring(repeater.ActualWidth())+
        L" marginRight="+std::to_wstring(state->originalMargin.Right));
    c::Border panel; panel.Name(L"TaskeeTaskbarPanel");panel.Height(44);
    panel.IsHitTestVisible(false);
    x::Automation::AutomationProperties::SetName(panel,L"Taskee stats · double-click to open options");
    panel.DoubleTapped([](auto const&,auto const& args) {
        HANDLE show=OpenEventW(EVENT_MODIFY_STATE,FALSE,L"Local\\Taskee.ShowOptions");
        if(show) {SetEvent(show);CloseHandle(show);}args.Handled(true);
    });
    panel.Opacity(0.0); // Wait for the native layout animation to settle.
    panel.HorizontalAlignment(x::HorizontalAlignment::Left);
    panel.VerticalAlignment(x::VerticalAlignment::Center);
    panel.Margin({state->expectedSlotRight-state->width,0,0,0});
    c::Grid::SetColumnSpan(panel,std::max(1,static_cast<int>(grid.ColumnDefinitions().Size())));
    state->panel=panel;
    // Inset the taskbar layout so app buttons and the native Weather control
    // have less available width. The panel occupies that reserved region.
    state->started=GetTickCount64();
    state->timer=x::DispatcherTimer(); state->timer.Interval(std::chrono::milliseconds(500));
    state->tickToken=state->timer.Tick([state](auto const&,auto const&) {
        try { Sample(state); } catch (...) { Log(L"Sample failed; restoring");try {Restore(state);state->failed=true;WriteStatus(L"Taskbar rendering failed · spacing restored");} catch (...) {Log(L"Restore failed");} }
    });
    bool current;
    {
        std::lock_guard lock(bindingMutex);
        current=binding.Current(ticket);
        if(current) reservation=state;
    }
    if(!current) {Detach(state);return false;}
    state->timer.Start();
    guard.committed=true;
    Log(L"CONNECTED native taskbar renderer");
    return true;
}
static void QueueWeatherTest(x::FrameworkElement const& weather,InstanceHandle weatherHandle,InstanceHandle frameHandle) {
    auto frame=FindFrame(weather);
    if (!frame) return;
    RECT rect{}; HWND tray=FindWindowW(L"Shell_TrayWnd",nullptr); GetWindowRect(tray,&rect);
    if (std::abs((rect.right-rect.left)*96.0/GetDpiForWindow(tray)-frame.ActualWidth())>3) return;
    std::optional<taskee::BindingTicket> ticket;
    {std::lock_guard lock(bindingMutex);ticket=binding.Queue(weatherHandle,frameHandle);}
    if(!ticket) return;
    try {
        weather.Dispatcher().RunAsync(u::CoreDispatcherPriority::Low,[weather,ticket=*ticket] {
            std::shared_ptr<Reservation> previous;
            {std::lock_guard lock(bindingMutex);if(!binding.Current(ticket)) return;previous=reservation;}
            // Primary taskbar controls share this dispatcher; release the old
            // inset before reading the new repeater's original margin.
            Detach(previous);
            try {if(Attach(weather,ticket)) return;}
            catch(...) {Log(L"Attach failed="+std::to_wstring(static_cast<unsigned>(winrt::to_hresult())));}
            std::lock_guard lock(bindingMutex);binding.Failed(ticket);
        });
    } catch(...) {std::lock_guard lock(bindingMutex);binding.Failed(*ticket);throw;}
}
static void QueueTaskbarRemoval(InstanceHandle handle) {
    std::shared_ptr<Reservation> previous;
    {
        std::lock_guard lock(bindingMutex);
        bool used=reservation && binding.Current(reservation->binding) &&
            std::find(reservation->controls.begin(),reservation->controls.end(),handle)!=reservation->controls.end();
        if(!binding.Removed(used?reservation->binding.weather:handle)) return;
        previous=reservation;
    }
    if(!previous || previous->stopped || !previous->frame) return;
    auto frame=previous->frame;
    frame.Dispatcher().RunAsync(u::CoreDispatcherPriority::Low,[previous,frame] {
        try {
            Detach(previous);
            // A newly bound tree may already have replaced this reservation.
            {std::lock_guard lock(bindingMutex);
                if(reservation==previous) {reservation.reset();WriteStatus(L"Waiting for Windows taskbar controls to return");}
            }
            // Add and Remove notifications can arrive in either order. Inspect
            // the settled tree too, so an earlier Add is not lost to de-duplication.
            if(frame.XamlRoot()) {
                auto weather=FindType(frame,L"Taskbar.AugmentedEntryPointButton");
                if(weather) QueueWeatherTest(weather,watcher->HandleOf(weather),watcher->HandleOf(frame));
            }
        } catch(...) {Log(L"Waiting for a replacement taskbar tree after removal");}
    });
}

class Tap : public winrt::implements<Tap, IObjectWithSite, winrt::non_agile> {
    winrt::com_ptr<IUnknown> site;
public:
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* value) noexcept override {
        try {
            site.copy_from(value);
            Log(L"SetSite");
            WriteStatus(L"Waiting for Weather · enable Widgets in Windows taskbar settings");
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
