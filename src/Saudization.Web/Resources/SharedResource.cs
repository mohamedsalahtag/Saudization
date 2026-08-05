namespace Saudization.Web;

/// <summary>
/// Marker type for IStringLocalizer&lt;SharedResource&gt;. It lives in the ROOT namespace
/// (Saudization.Web) on purpose: with ResourcesPath="Resources" the localizer then resolves
/// keys from Resources/SharedResource.{culture}.resx. Placing it under Saudization.Web.Resources
/// would make it look in Resources/Resources/ and silently fall back to the key name.
/// </summary>
public class SharedResource { }
