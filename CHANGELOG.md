# Changelog

All notable changes to this project are documented in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-11-17

### Changed

- `Paginate` and `IteratePagesAsync` throw when a page request fails. They previously
  ended the enumeration silently, which made a failure look like an empty or complete
  result. `PaginationErrorMode.Stop` and `PaginationErrorMode.Skip` restore the old
  behaviors, and `Skip` still throws on an authentication failure.
- Reaching the pagination page limit throws `DaktelaPaginationLimitException` instead of
  returning a truncated result. The limit is configurable through `DaktelaConfig.MaxPages`.
- `DaktelaConfig` is captured when the client is created; later changes to `AccessToken`,
  `AuthMethod`, or `UserAgentSuffix` no longer affect an existing client.
- An invalid `UserAgentSuffix` or an access token that cannot be sent as a header throws
  `ArgumentException` from the constructor instead of `FormatException` on every request.
- The request timeout now covers reading the response body. A connection failure while
  reading the body throws `DaktelaConnectionException` and follows the retry policy.
- The connector's own transport uses `SocketsHttpHandler` without cookie storage and
  recycles pooled connections every five minutes.
- `IterateAsync` is implemented on top of the paginator.

### Security

- Removed the access token from exception messages, `ResponseBody`, and `Errors` when a
  server echoes it, in its plain, URL-encoded, and JSON-escaped forms. A connection error
  that quotes the request URL, which carries the token with query parameter
  authentication, is redacted and its underlying exception is not chained.

- Stopped following redirects on the connector's own transport, which forwarded the
  `X-AUTH-TOKEN` header to the redirect target, including one on another host. A 3xx
  response now throws a `DaktelaException` naming the target. With an injected `HttpClient`
  that followed a redirect to another origin, the connector throws instead of using the
  response.

### Added

- `DaktelaConfig.Login` with `DaktelaLogin`, `ITokenStore`, `InMemoryTokenStore`, and
  `TokenSet` to start a session with a username and password instead of a static access
  token. The connector refreshes the short-lived access token before it expires and after
  the server rejects it, logs in again when the session has ended, and sends no further
  login after the server rejects the credentials. The password and the session tokens are
  redacted from exceptions. `DaktelaConfig.AccessToken` is no longer a `required` member.
- `PaginationErrorMode` and `errorMode` parameters on `Paginate` and `IteratePagesAsync`.
- `DaktelaConfig.MaxPages`, `DaktelaConfig.ServerTimeZone`, and
  `DaktelaConfig.IncludeNullValues`.
- `IDaktelaClient`, implemented by `DaktelaClient`, and a public `DaktelaPaginator<T>`
  constructor so the client can be substituted in tests.
- `DaktelaForbiddenException` for HTTP 403 and `DaktelaPaginationLimitException`.
- Symbol package and SourceLink metadata.

### Fixed

- An endpoint with a leading slash, such as `/users`, was rejected on Linux and macOS.
- `Filter.In` and `Filter.NotIn` encoded a single collection argument (`int[]`,
  `List<string>`, ...) as a nested list.
- Empty-string and `0000-00-00` date values failed the whole response when read into a
  `DateTime?` or `DateTimeOffset?` property; they now read as null.

### Deprecated

- The `Paginate` and `IteratePagesAsync` overloads taking `bool stopOnError`.

## [1.2.0]

Raw CRUD paths, paginator helpers, health checks, rate-limit policy, retry configuration,
response helpers, phone formatting, User-Agent customization, and canonical filter
encoding.

## [1.1.0]

Fixed `/api/v6/` base path handling, response envelope unwrapping, safe-method-only
retries, stricter configuration and endpoint validation, and corrected query encoding.
