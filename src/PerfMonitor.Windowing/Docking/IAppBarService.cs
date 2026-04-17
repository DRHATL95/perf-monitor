using System.Windows;

namespace PerfMonitor.Windowing.Docking;

public interface IAppBarService
{
    bool Register(Window window, AppBarEdge edge, int thicknessPx);
    void Unregister(Window window);
}
