using System;
using BepInEx.Configuration;
using UnityEngine;

namespace MapTracker
{
    internal enum MapTargetAction { None, Select, Place, Clear, Expand, Consume }
    internal enum NavigationBindingAction { None, Select, Share }
    internal static class MapTargetGesture
    {
        internal static NavigationBindingAction BindingAction(KeyboardShortcut select, KeyboardShortcut share,
            KeyCode button, Func<KeyCode, bool> pressed, bool sharing)
        {
            if (sharing && Matches(share, button, pressed)) return NavigationBindingAction.Share;
            return Matches(select, button, pressed) ? NavigationBindingAction.Select : NavigationBindingAction.None;
        }
        internal static bool Matches(KeyboardShortcut shortcut, KeyCode button, Func<KeyCode, bool> pressed)
        {
            if (shortcut.MainKey == KeyCode.None || shortcut.MainKey != button) return false;
            foreach (var modifier in shortcut.Modifiers)
            {
                bool held = pressed(modifier);
                if (modifier == KeyCode.LeftAlt || modifier == KeyCode.RightAlt) held |= pressed(KeyCode.LeftAlt) || pressed(KeyCode.RightAlt);
                if (!held) return false;
            }
            return true;
        }
        internal static MapTargetAction Resolve(bool navigation, bool selectClick, bool hit, bool sameTarget, bool collapsedGroup)
        {
            if (!navigation) return MapTargetAction.None;
            // A double-click on a navigation-bound button must not create a native pin.
            if (!selectClick) return MapTargetAction.Consume;
            if (sameTarget) return MapTargetAction.Clear;
            if (!hit) return MapTargetAction.Place;
            if (collapsedGroup) return MapTargetAction.Expand;
            return MapTargetAction.Select;
        }
    }
}
