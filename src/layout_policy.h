#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <map>
#include <optional>
#include <vector>

namespace taskee {
// Diagnostics roots are XAML host objects. In-process automation coordinates
// are relative to those hosts, so identify the root through visual ancestry.
class VisualTreeAncestry {
    std::map<std::uint64_t,std::uint64_t> parents;
public:
    void Added(std::uint64_t handle,std::uint64_t parent) {parents[handle]=parent;}
    void Removed(std::uint64_t handle) noexcept {parents.erase(handle);}
    std::vector<std::uint64_t> Path(std::uint64_t handle) const {
        std::vector<std::uint64_t> result;
        while(handle) {
            auto found=parents.find(handle);
            if(found==parents.end() || result.size()>=128 ||
                std::find(result.begin(),result.end(),handle)!=result.end()) return {};
            result.push_back(handle);handle=found->second;
        }
        return result;
    }
};
inline bool MonitorSelected(unsigned number,bool second,bool third) noexcept {
    return number==1 || (number==2 && second) || (number==3 && third);
}
inline double PanelWidth(double wanted, double minimum, double maximum, double slotRight) noexcept {
    double available=std::max(0.0,std::floor(slotRight-160.0));
    double upper=std::min(maximum,available);
    double lower=std::min(minimum,upper);
    return std::ceil(std::clamp(wanted,lower,upper));
}

class LayoutValidation {
    std::optional<std::uint64_t> badSince;
public:
    bool Observe(bool good,std::uint64_t now) noexcept {
        if(good) {Reset();return false;}
        if(!badSince) badSince=now;
        return now-*badSince>3500;
    }
    void Reset() noexcept {badSince.reset();}
};

struct BindingTicket {
    std::uint64_t version, weather, frame;
};
// Used under the renderer's mutex. Stale dispatcher work must not reattach
// controls after a removal, replacement, or owner shutdown.
class TaskbarBinding {
    std::uint64_t version{},weather{},frame{};
    bool stopped{};
public:
    std::optional<BindingTicket> Queue(std::uint64_t nextWeather,std::uint64_t nextFrame) noexcept {
        if(stopped || !nextWeather || (weather==nextWeather && frame==nextFrame)) return std::nullopt;
        weather=nextWeather;frame=nextFrame;
        return BindingTicket{++version,weather,frame};
    }
    bool Current(BindingTicket ticket) const noexcept {
        return !stopped && ticket.version==version && ticket.weather==weather && ticket.frame==frame;
    }
    void Failed(BindingTicket ticket) noexcept {if(Current(ticket)) {weather=frame=0;++version;}}
    bool Removed(std::uint64_t handle) noexcept {
        if(!handle || (handle!=weather && handle!=frame)) return false;
        weather=frame=0;++version;return true;
    }
    void Stop() noexcept {stopped=true;weather=frame=0;++version;}
};
}
