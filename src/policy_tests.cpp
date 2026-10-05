#include "layout_policy.h"
#include <cstdio>
#include <stdexcept>
static unsigned checks;
static void Check(bool condition,char const* name) {
    if(!condition) throw std::runtime_error(name);
    ++checks;std::printf("PASS %s\n",name);
}
int main() {
    try {
        Check(taskee::PanelWidth(300,600,620,500)==340,"Narrow slot overrides an oversized minimum without reversed bounds");
        Check(taskee::PanelWidth(300,0,620,159)==0,"No available space produces no native panel");
        Check(taskee::PanelWidth(400.1,0,620,560.7)==400,"Fractional slot bounds cannot round beyond the available space");
        Check(taskee::PanelWidth(259,0,620,2200)==259,"Normal taskbar width remains unchanged");
        bool bounded=true;
        for(int slot=0;slot<=1000;slot+=13) for(int minimum=0;minimum<=600;minimum+=100) {
            double width=taskee::PanelWidth(500,minimum,620,slot);
            bounded=bounded && width>=0 && width<=std::max(0,slot-160);
        }
        Check(bounded,"Width remains bounded across narrow layouts and permitted minimums");
        taskee::LayoutValidation validation;
        Check(!validation.Observe(false,0),"A new invalid layout receives a settling interval");
        bool expired=false;
        // Every observation represents a new resized layout. Width changes do
        // not reset the validation clock in the production renderer.
        for(unsigned now=500;now<=4000;now+=500) expired=validation.Observe(false,now);
        Check(expired,"Continually changing layouts still expire after 3.5 seconds");
        Check(!validation.Observe(true,4500)&&!validation.Observe(false,5000),"A valid layout resets the failure interval");
        validation.Reset();Check(!validation.Observe(false,10000),"A detached reservation gets a fresh validation interval");
        taskee::TaskbarBinding binding;
        auto first=binding.Queue(1,10);
        Check(first&&binding.Current(*first)&&!binding.Queue(1,10),"Duplicate callbacks do not queue another attachment");
        Check(!binding.Removed(99)&&binding.Current(*first),"Unrelated visual removals preserve the active binding");
        Check(binding.Removed(1)&&!binding.Current(*first),"Weather removal invalidates pending dispatcher work");
        auto second=binding.Queue(2,10);
        Check(second&&binding.Current(*second),"Replacement Weather can bind without restarting Explorer");
        auto third=binding.Queue(3,11);
        Check(third&&binding.Current(*third)&&!binding.Current(*second),"A newer replacement supersedes an older queued callback");
        Check(!binding.Removed(2)&&binding.Current(*third),"A late removal of old Weather cannot detach its replacement");
        Check(binding.Removed(11)&&!binding.Current(*third),"Taskbar frame removal also invalidates the binding");
        auto failed=binding.Queue(4,12);binding.Failed(*failed);
        Check(binding.Queue(4,12).has_value(),"Failed attachment can be retried");
        auto stopped=binding.Queue(5,12);binding.Stop();
        Check(!binding.Current(*stopped)&&!binding.Queue(6,13),"Owner shutdown prevents stale callbacks from attaching");
        std::printf("%u native policy checks passed.\n",checks);return 0;
    } catch(std::exception const& ex) {std::fprintf(stderr,"FAIL %s\n",ex.what());return 1;}
}
