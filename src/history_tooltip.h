#pragma once
#include <chrono>
#include <functional>
#include <optional>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>

namespace taskee {
namespace hx=winrt::Windows::UI::Xaml;
namespace hc=hx::Controls;
namespace hj=winrt::Windows::Data::Json;

struct HistoryTooltip : std::enable_shared_from_this<HistoryTooltip> {
    hc::ToolTip tip;
    hc::StackPanel content;
    hx::DispatcherTimer delay;
    winrt::weak_ref<hx::FrameworkElement> target;
    hj::JsonObject item{nullptr};
    bool hovered{};
    std::function<void(std::wstring const&)> diagnostic;
    static hx::Media::SolidColorBrush Color(uint8_t red,uint8_t green,uint8_t blue) {
        return hx::Media::SolidColorBrush(winrt::Windows::UI::Color{255,red,green,blue});
    }
    static hx::Media::SolidColorBrush HexColor(winrt::hstring const& color) {
        std::wstring hex(color);unsigned long rgba=0xD8E3EF;
        if(hex.size()==7||hex.size()==9) rgba=std::stoul(hex.substr(1),nullptr,16);
        return Color(static_cast<uint8_t>(rgba>>16),static_cast<uint8_t>(rgba>>8),static_cast<uint8_t>(rgba));
    }
    static hc::TextBlock Label(winrt::hstring const& value,double size=11) {
        hc::TextBlock text;text.Text(value);text.FontFamily(hx::Media::FontFamily(L"Segoe UI"));text.FontSize(size);
        text.Foreground(Color(152,164,185));text.TextWrapping(hx::TextWrapping::Wrap);return text;
    }
    static std::shared_ptr<HistoryTooltip> Create(hx::FrameworkElement const& target,std::function<void(std::wstring const&)> diagnostic={}) {
        auto result=std::make_shared<HistoryTooltip>();result->target=target;
        result->diagnostic=std::move(diagnostic);
        result->content.Width(320);result->tip.Content(result->content);
        result->tip.Padding({14});result->tip.MaxWidth(360);
        result->tip.Background(Color(26,32,44));result->tip.Foreground(Color(236,240,248));result->tip.BorderBrush(Color(80,96,119));
        result->tip.Placement(hc::Primitives::PlacementMode::Top);
        // PlacementTarget only controls position. SetToolTip assigns the owner
        // required by IsOpen; without it Windows throws and no popup appears.
        hc::ToolTipService::SetToolTip(target,result->tip);
        std::weak_ptr<HistoryTooltip> weak=result;
        // Open explicitly so the graph stays visible for the entire hover;
        // ToolTipService would close it after its fixed display timeout.
        result->delay.Interval(std::chrono::milliseconds(400));
        result->delay.Tick([weak](auto const&,auto const&) {
            if(auto live=weak.lock()) live->Open();
        });
        result->tip.Opened([weak](auto const&,auto const&) {if(auto live=weak.lock()) {try {live->Render();}catch(...) {live->ReportFault(L"render");live->Close();}}});
        result->tip.Closed([weak](auto const&,auto const&) {
            if(auto live=weak.lock();live&&live->hovered) {
                try {if(auto element=live->target.get()) element.Dispatcher().RunAsync(winrt::Windows::UI::Core::CoreDispatcherPriority::Normal,
                    [weak] {if(auto current=weak.lock();current&&current->hovered) current->Open();});}
                catch(...) {live->ReportFault(L"reopen");live->Close();}
            }
        });
        target.PointerEntered([weak](auto const&,auto const&) {if(auto live=weak.lock()) {try {live->hovered=true;live->delay.Start();}catch(...) {live->ReportFault(L"hover");live->Close();}}});
        target.PointerExited([weak](auto const&,auto const&) {if(auto live=weak.lock()) live->Close();});
        target.PointerPressed([weak](auto const&,auto const&) {if(auto live=weak.lock()) live->Close();});
        return result;
    }
    void Close() noexcept {hovered=false;try {delay.Stop();tip.IsOpen(false);}catch(...) {}}
    void Open() noexcept {
        try {delay.Stop();if(!hovered) return;if(auto element=target.get();element&&element.IsLoaded()) {
            tip.XamlRoot(element.XamlRoot());tip.PlacementTarget(element);Render();tip.IsOpen(true);
        }} catch(...) {ReportFault(L"open");Close();}
    }
    void ReportFault(wchar_t const* action) noexcept {
        if(!diagnostic) return;
        try {diagnostic(L"History tooltip "+std::wstring(action)+L" failed="+std::to_wstring(static_cast<unsigned>(winrt::to_hresult())));}catch(...) {}
    }
    void Update(hj::JsonObject const& value) {item=value;if(tip.IsOpen()) Render();}
    void Render() {
        if(!item) return;
        content.Children().Clear();auto graphValue=item.GetNamedValue(L"history",hj::JsonValue::CreateNullValue());
        auto graph=graphValue.ValueType()==hj::JsonValueType::Object?graphValue.GetObject():hj::JsonObject{nullptr};
        if(!graph) {content.Children().Append(Label(item.GetNamedString(L"tooltip",L"")));return;}
        auto title=Label(graph.GetNamedString(L"title",L""),13);title.Foreground(Color(236,240,248));content.Children().Append(title);
        auto accent=HexColor(item.GetNamedString(L"color",L"#D8E3EF"));
        auto current=Label(graph.GetNamedString(L"current",L"—"),23);current.Foreground(accent);current.Margin({0,4,0,8});content.Children().Append(current);
        constexpr double width=246,height=128,padding=4;
        hc::Grid plot;plot.Height(height);
        hc::ColumnDefinition axis;axis.Width({74,hx::GridUnitType::Pixel});plot.ColumnDefinitions().Append(axis);
        hc::ColumnDefinition drawing;drawing.Width({width,hx::GridUnitType::Pixel});plot.ColumnDefinitions().Append(drawing);
        auto tickValue=graph.GetNamedValue(L"ticks",hj::JsonValue::CreateNullValue());
        auto ticks=tickValue.ValueType()==hj::JsonValueType::Array?tickValue.GetArray():hj::JsonArray{nullptr};
        uint32_t count=ticks?ticks.Size():0;
        if(count>=2&&count<=9) {
            for(uint32_t i=0;i<count;i++) {
                auto label=Label(ticks.GetStringAt(i));
                label.HorizontalAlignment(hx::HorizontalAlignment::Right);label.TextAlignment(hx::TextAlignment::Right);
                label.VerticalAlignment(i==count-1?hx::VerticalAlignment::Bottom:hx::VerticalAlignment::Top);
                double top=i>0&&i<count-1?padding+i*(height-2*padding)/(count-1)-7:0;
                label.Margin({0,top,8,0});
                plot.Children().Append(label);
            }
        } else {
            auto top=Label(graph.GetNamedString(L"top",L"—"));top.VerticalAlignment(hx::VerticalAlignment::Top);top.HorizontalAlignment(hx::HorizontalAlignment::Right);top.Margin({0,0,8,0});plot.Children().Append(top);
            auto bottom=Label(graph.GetNamedString(L"bottom",L"—"));bottom.VerticalAlignment(hx::VerticalAlignment::Bottom);bottom.HorizontalAlignment(hx::HorizontalAlignment::Right);bottom.Margin({0,0,8,0});plot.Children().Append(bottom);
        }
        hc::Canvas canvas;canvas.Width(width);canvas.Height(height);hc::Grid::SetColumn(canvas,1);plot.Children().Append(canvas);
        uint32_t tickCount=count>=2&&count<=9?count:5;
        for(uint32_t i=0;i<tickCount;i++) {
            hx::Shapes::Line line;line.X1(padding);line.X2(width-padding);line.Y1(padding+i*(height-2*padding)/(tickCount-1));line.Y2(line.Y1());line.Stroke(Color(52,65,81));line.StrokeThickness(1);canvas.Children().Append(line);
        }
        for(int i=0;i<=5;i++) {
            hx::Shapes::Line line;line.X1(padding+i*(width-2*padding)/5);line.X2(line.X1());line.Y1(height-padding);line.Y2(height-1);line.Stroke(Color(52,65,81));line.StrokeThickness(1);canvas.Children().Append(line);
        }
        auto thresholdsValue=graph.GetNamedValue(L"thresholds",hj::JsonValue::CreateNullValue());
        if(thresholdsValue.ValueType()==hj::JsonValueType::Array) {
            auto thresholds=thresholdsValue.GetArray();
            for(uint32_t i=0;i<std::min(thresholds.Size(),2u);i++) {
                auto threshold=thresholds.GetObjectAt(i);double y=threshold.GetNamedNumber(L"y");
                if(!std::isfinite(y)||y<0||y>1000) continue;
                hx::Shapes::Line line;line.X1(padding);line.X2(width-padding);line.Y1(padding+(1000-y)*(height-2*padding)/1000);line.Y2(line.Y1());
                line.Stroke(HexColor(threshold.GetNamedString(L"color",L"#FBBF24")));line.StrokeThickness(1);line.Opacity(.8);
                hx::Media::DoubleCollection dashes;dashes.Append(4);dashes.Append(3);line.StrokeDashArray(dashes);
                hx::Automation::AutomationProperties::SetName(line,threshold.GetNamedString(L"label",L""));canvas.Children().Append(line);
            }
        }
        auto screenPoint=[&](double x,double y) {return winrt::Windows::Foundation::Point {
            static_cast<float>(padding+x*(width-2*padding)/1000),static_cast<float>(padding+(1000-y)*(height-2*padding)/1000)};};
        std::optional<winrt::Windows::Foundation::Point> latest;
        auto latestValue=graph.GetNamedValue(L"latest",hj::JsonValue::CreateNullValue());
        if(latestValue.ValueType()==hj::JsonValueType::Object) {
            auto point=latestValue.GetObject();auto y=point.GetNamedValue(L"y");
            if(y.ValueType()==hj::JsonValueType::Number) {
                double px=point.GetNamedNumber(L"x"),py=y.GetNumber();
                if(std::isfinite(px)&&std::isfinite(py)&&px>=0&&px<=1000&&py>=0&&py<=1000) latest=screenPoint(px,py);
            }
        }
        auto points=graph.GetNamedArray(L"points");if(points.Size()>240) throw winrt::hresult_invalid_argument();
        hx::Shapes::Polyline segment;segment.Stroke(accent);segment.StrokeThickness(2);
        auto flush=[&] {
            if(segment.Points().Size()>1) canvas.Children().Append(segment);
            else if(segment.Points().Size()==1) {
                auto point=segment.Points().GetAt(0);
                if(!latest||point.X!=latest->X||point.Y!=latest->Y) {
                    hx::Shapes::Ellipse dot;dot.Width(4);dot.Height(4);dot.Fill(accent);
                    hc::Canvas::SetLeft(dot,point.X-2);hc::Canvas::SetTop(dot,point.Y-2);canvas.Children().Append(dot);
                }
            }
            segment=hx::Shapes::Polyline();segment.Stroke(accent);segment.StrokeThickness(2);
        };
        for(uint32_t i=0;i<points.Size();i++) {
            auto point=points.GetObjectAt(i);auto y=point.GetNamedValue(L"y");
            if(y.ValueType()==hj::JsonValueType::Null) {flush();continue;}
            double px=point.GetNamedNumber(L"x"),py=y.GetNumber();
            if(!std::isfinite(px)||!std::isfinite(py)||px<0||px>1000||py<0||py>1000) {flush();continue;}
            segment.Points().Append(screenPoint(px,py));
        }
        flush();
        if(latest) {
            hx::Shapes::Ellipse dot;dot.Width(7);dot.Height(7);dot.Fill(accent);dot.Stroke(Color(26,32,44));dot.StrokeThickness(1.5);
            hc::Canvas::SetLeft(dot,latest->X-3.5);hc::Canvas::SetTop(dot,latest->Y-3.5);canvas.Children().Append(dot);
        }
        content.Children().Append(plot);
        hc::Grid time;time.Margin({74,5,0,10});hc::Canvas timeLabels;timeLabels.Width(width);timeLabels.Height(16);time.Children().Append(timeLabels);
        for(int i=0;i<=5;i++) {
            auto label=Label(i==5?L"Now":winrt::hstring(L"−"+std::to_wstring(5-i)+L"m"),10);label.Width(40);
            label.TextAlignment(i==0?hx::TextAlignment::Left:i==5?hx::TextAlignment::Right:hx::TextAlignment::Center);
            double x=padding+i*(width-2*padding)/5;
            hc::Canvas::SetLeft(label,i==0?padding:i==5?width-padding-40:x-20);timeLabels.Children().Append(label);
        }
        content.Children().Append(time);
        auto summary=Label(graph.GetNamedString(L"summary",L""));summary.Foreground(Color(220,227,237));content.Children().Append(summary);
    }
};
}
