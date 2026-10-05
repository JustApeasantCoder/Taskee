#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <optional>

namespace taskee {
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
