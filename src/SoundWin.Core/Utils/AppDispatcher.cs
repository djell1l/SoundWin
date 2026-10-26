using System;

namespace SoundWin.Core.Utils;

public static class AppDispatcher
{
    public static Action<Action>? RunOnUiThread { get; set; }

    public static void Dispatch(Action action)
    {
        if (RunOnUiThread != null)
        {
            RunOnUiThread(action);
        }
        else
        {
            action();
        }
    }
}
