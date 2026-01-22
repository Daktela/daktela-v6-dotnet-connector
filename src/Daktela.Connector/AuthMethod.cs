namespace Daktela.Connector;

/// <summary>
/// Specifies the authentication method used to authenticate API requests.
/// </summary>
public enum AuthMethod
{
    /// <summary>
    /// Authentication via X-AUTH-TOKEN header (recommended).
    /// </summary>
    Header,

    /// <summary>
    /// Authentication via accessToken query parameter.
    /// </summary>
    QueryParam,

    /// <summary>
    /// Authentication via c_user cookie.
    /// </summary>
    Cookie
}
