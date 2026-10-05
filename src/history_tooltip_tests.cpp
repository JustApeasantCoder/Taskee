#include <windows.h>
#include <windows.ui.xaml.hosting.desktopwindowxamlsource.h>
#undef GetCurrentTime
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>
#include <winrt/Windows.UI.Xaml.Input.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.Data.Json.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <memory>
#include <string>
#include "history_tooltip.h"

static void Check(bool passed,char const* description) {
    if(!passed) throw std::runtime_error(description);
    printf("PASS %s\n",description);
}
int main() {
    try {
        winrt::init_apartment(winrt::apartment_type::single_threaded);
        auto manager=winrt::Windows::UI::Xaml::Hosting::WindowsXamlManager::InitializeForCurrentThread();
        // A private, hidden XAML island exercises real Windows ToolTip APIs.
        // It does not attach to Explorer, display a taskbar, or move the pointer.
        HWND window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"Taskee history checks",WS_OVERLAPPED,
            -10000,-10000,400,200,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        winrt::check_bool(window!=nullptr);
        auto source=winrt::Windows::UI::Xaml::Hosting::DesktopWindowXamlSource();
        winrt::check_hresult(source.as<IDesktopWindowXamlSourceNative>()->AttachToWindow(window));
        taskee::hc::TextBlock target;target.Text(L"CPU 61%");source.Content(target);

        taskee::hc::ToolTip unowned;unowned.Content(winrt::box_value(L"Unowned"));
        unowned.XamlRoot(target.XamlRoot());unowned.PlacementTarget(target);
        bool rejected=false;
        try {unowned.IsOpen(true);}catch(winrt::hresult_error const&) {rejected=true;}
        Check(rejected,"Windows rejects opening a positioned tooltip without an owner");

        auto tooltip=taskee::HistoryTooltip::Create(target);
        auto owner=taskee::hc::ToolTipService::GetToolTip(target).try_as<taskee::hc::ToolTip>();
        Check(owner&&owner==tooltip->tip,"History tooltip is registered to its stat before opening");
        auto item=taskee::hj::JsonObject::Parse(LR"({"color":"#A5B4FC","history":{"title":"CPU usage","current":"61%","top":"100%","bottom":"0%","ticks":["100%","75%","50%","25%","0%"],"latest":{"x":1000,"y":610},"thresholds":[{"y":800,"color":"#FBBF24","label":"Warning 80%"},{"y":950,"color":"#FB7185","label":"Critical 95%"}],"summary":"Low 17% · High 96%","points":[{"x":0,"y":170},{"x":400,"y":960},{"x":500,"y":null},{"x":1000,"y":610}]}})");
        tooltip->Update(item);tooltip->Render();
        Check(tooltip->content.Children().Size()==5,"Native graph contains only title, value, plot, time scale, and extrema");
        auto plot=tooltip->content.Children().GetAt(2).as<taskee::hc::Grid>();
        wchar_t const* labels[]={L"100%",L"75%",L"50%",L"25%",L"0%"};
        bool labeled=plot.Children().Size()==6;
        for(uint32_t i=0;i<5&&labeled;i++) labeled=plot.Children().GetAt(i).as<taskee::hc::TextBlock>().Text()==labels[i];
        auto canvas=plot.Children().GetAt(5).as<taskee::hc::Canvas>();
        bool aligned=canvas.Children().Size()>=5;
        for(uint32_t i=0;i<5&&aligned;i++) {
            auto line=canvas.Children().GetAt(i).as<taskee::hx::Shapes::Line>();
            aligned=line.Y1()==4+i*30&&line.Y2()==line.Y1();
        }
        Check(labeled&&aligned&&plot.Height()==128&&plot.Children().GetAt(0).as<taskee::hc::TextBlock>().Margin().Right==8,"Native taller graph renders five grid lines with labels aligned close to the plot");
        auto time=tooltip->content.Children().GetAt(3).as<taskee::hc::Grid>().Children().GetAt(0).as<taskee::hc::Canvas>();
        wchar_t const* minutes[]={L"−5m",L"−4m",L"−3m",L"−2m",L"−1m",L"Now"};
        bool timeLabels=time.Children().Size()==6;
        for(uint32_t i=0;i<6&&timeLabels;i++) timeLabels=time.Children().GetAt(i).as<taskee::hc::TextBlock>().Text()==minutes[i];
        Check(timeLabels,"Native graph labels every minute from five minutes ago to now");
        int markers=0;
        for(auto const& child:canvas.Children()) if(auto dot=child.try_as<taskee::hx::Shapes::Ellipse>();dot&&dot.Width()==7) {
            markers++;Check(std::abs(taskee::hc::Canvas::GetLeft(dot)-238.5)<.001,"Native latest marker follows the last sample without clipping the edge");
        }
        if(markers!=1) throw std::runtime_error("Expected one native latest marker");
        auto warning=canvas.Children().GetAt(11).as<taskee::hx::Shapes::Line>();
        auto critical=canvas.Children().GetAt(12).as<taskee::hx::Shapes::Line>();
        auto warningColor=warning.Stroke().as<taskee::hx::Media::SolidColorBrush>().Color();
        auto criticalColor=critical.Stroke().as<taskee::hx::Media::SolidColorBrush>().Color();
        Check(warning.Y1()==28&&critical.Y1()==10&&warning.StrokeDashArray().Size()==2&&critical.StrokeDashArray().Size()==2
            &&warningColor.R==251&&warningColor.G==191&&criticalColor.R==251&&criticalColor.B==133,"Native alert lines are dashed and use the configured positions and colors");
        tooltip->tip.XamlRoot(target.XamlRoot());tooltip->tip.PlacementTarget(target);tooltip->tip.IsOpen(true);
        Check(tooltip->tip.IsOpen(),"An owned native history graph opens successfully");
        tooltip->Close();
        Check(!tooltip->tip.IsOpen(),"Closing the graph leaves no popup open");
        item.GetNamedObject(L"history").SetNamedValue(L"latest",taskee::hj::JsonValue::CreateNullValue());tooltip->Render();
        auto staleCanvas=tooltip->content.Children().GetAt(2).as<taskee::hc::Grid>().Children().GetAt(5).as<taskee::hc::Canvas>();
        bool noLiveMarker=true;
        for(auto const& child:staleCanvas.Children()) if(auto dot=child.try_as<taskee::hx::Shapes::Ellipse>();dot&&dot.Width()==7) noLiveMarker=false;
        Check(noLiveMarker,"Native unavailable readings do not retain the live endpoint marker");
        item.GetNamedObject(L"history").SetNamedValue(L"ticks",taskee::hj::JsonArray::Parse(LR"(["100%","80%","60%","40%","20%","0%"])") );tooltip->Render();
        auto sixPlot=tooltip->content.Children().GetAt(2).as<taskee::hc::Grid>();
        auto sixCanvas=sixPlot.Children().GetAt(6).as<taskee::hc::Canvas>();
        Check(sixPlot.Children().Size()==7&&sixCanvas.Children().GetAt(5).as<taskee::hx::Shapes::Line>().Y1()==124,"Rounded scales with six labels keep each grid line aligned");
        item.GetNamedObject(L"history").Remove(L"latest");item.GetNamedObject(L"history").Remove(L"thresholds");
        item.GetNamedObject(L"history").SetNamedValue(L"ticks",taskee::hj::JsonValue::CreateNullValue());
        tooltip->Update(item);tooltip->Render();
        bool emptyFallback=tooltip->content.Children().GetAt(2).as<taskee::hc::Grid>().Children().Size()==3;
        item.GetNamedObject(L"history").Remove(L"ticks");tooltip->Render();
        Check(emptyFallback&&tooltip->content.Children().GetAt(2).as<taskee::hc::Grid>().Children().Size()==3,"Older or empty snapshots retain endpoint labels when ticks are unavailable");
        item.SetNamedValue(L"history",taskee::hj::JsonValue::CreateNullValue());item.SetNamedValue(L"tooltip",taskee::hj::JsonValue::CreateStringValue(L"Legacy reading"));tooltip->Render();
        Check(tooltip->content.Children().Size()==1&&tooltip->content.Children().GetAt(0).as<taskee::hc::TextBlock>().Text()==L"Legacy reading","Explicit null history payloads fall back to the legacy tooltip safely");
        taskee::hc::ToolTipService::SetToolTip(target,nullptr);tooltip.reset();source.Close();DestroyWindow(window);manager.Close();
        printf("13 native history checks passed.\n");return 0;
    } catch(winrt::hresult_error const& error) {
        fwprintf(stderr,L"Native history checks failed: 0x%08X %s\n",static_cast<unsigned>(error.code().value),error.message().c_str());return 1;
    } catch(std::exception const& error) {fprintf(stderr,"Native history checks failed: %s\n",error.what());return 1;}
}
