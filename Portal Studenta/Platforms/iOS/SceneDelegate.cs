using Foundation;
using UIKit;

namespace Portal_Studenta;

[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
    [Export("scene:openURLContexts:")]
    public void OpenUrlContexts(UIScene scene, NSSet<UIOpenUrlContext> urlContexts)
    {
        try
        {
            var url = urlContexts.ToArray().FirstOrDefault()?.Url;
            if (url != null)
            {
                Console.WriteLine($"[SceneDelegate] OpenUrlContexts: {url.Scheme}://...");
                Microsoft.Maui.ApplicationModel.Platform.OpenUrl(UIApplication.SharedApplication, url, new NSDictionary());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SceneDelegate] Error in OpenUrlContexts: {ex.Message}");
        }
    }
}
