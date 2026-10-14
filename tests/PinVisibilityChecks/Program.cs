using MapTracker;
using UnityEngine;

static class Checks
{
    static void Check(bool valid, string label) { if (!valid) throw new Exception(label); System.Console.WriteLine("PASS: " + label); }
    static void Main()
    {
        var root = new GameObject(); var mask = new NativeNameVisibility();
        for (int frame = 0; frame < 1000; frame++) { root.SetActive(true); mask.Apply(root, false); }
        Check(root.Activations == 0 && root.Mask.alpha == 0, "native name refreshes and collision hiding never alternate activation state");
        int before = root.Activations;
        for (int frame = 0; frame < 1000; frame++) mask.Apply(root, true);
        Check(root.Activations == before && root.Mask.alpha == 1, "unchanged visible names need no repeated activation");
        root.SetActive(false); mask.Apply(root, true);
        Check(root.activeSelf && root.Mask.alpha == 1, "a newly allowed name can restore an inactive native root");
        var replaced = new GameObject(); mask.Apply(replaced, false);
        Check(!root.activeSelf && replaced.Mask.alpha == 0 && UnityEngine.Object.Destroyed == 1, "a native marker replacement releases its old mask");
        mask.Dispose();
        Check(!replaced.activeSelf && UnityEngine.Object.Destroyed == 2 && replaced.Mask.alpha == 1,
            "disposing the name visibility layer restores alpha before releasing its owned component");
    }
}
namespace UnityEngine
{
    public class Object { public static int Destroyed; public static void Destroy(Object value) { Destroyed++; } }
    public class CanvasGroup : Object { public bool interactable, blocksRaycasts; public float alpha=1; }
    public class GameObject : Object
    {
        public bool activeSelf=true; public int Activations; public CanvasGroup Mask;
        public void SetActive(bool value) { if(activeSelf!=value)Activations++; activeSelf=value; }
        public T AddComponent<T>() where T:new() {var component=new T();Mask=component as CanvasGroup;return component;}
    }
}
