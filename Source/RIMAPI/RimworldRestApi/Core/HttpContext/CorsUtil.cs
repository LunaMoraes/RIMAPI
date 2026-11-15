using System;
using System.Collections.Generic;
using System.Net;
using RIMAPI.Http;

namespace RIMAPI.Core
{
    public static class CorsUtil
    {
        private const string DefaultAllowedMethods = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
        private const string DefaultAllowedHeaders =
            "Content-Type, Accept, Authorization, ETag, If-None-Match, X-RimApi-Actor, X-RimApi-Role";

        public static bool IsAllowedLocalOrigin(string origin, ISet<string> allowedOrigins = null)
        {
            if (string.IsNullOrEmpty(origin))
                return true;

            // Local file:// pages (such as opening dashboard/index.html directly) send Origin: null
            if (string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase))
                return true;

            if (allowedOrigins != null && allowedOrigins.Contains(origin))
                return true;

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;

            string host = uri.Host;
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || host.Equals("::1", StringComparison.OrdinalIgnoreCase)
                || host.Equals("[::1]", StringComparison.OrdinalIgnoreCase);
        }

        public static void WriteCors(
            RimApiRequest req,
            RimApiResponse res,
            ISet<string> allowedOrigins = null
        )
        {
            var origin = req.Headers["Origin"];

            if (string.IsNullOrEmpty(origin))
            {
                res.Headers.Set("Access-Control-Allow-Origin", "*");
            }
            else if (IsAllowedLocalOrigin(origin, allowedOrigins))
            {
                res.Headers.Set("Access-Control-Allow-Origin", origin);
                res.Headers.Set("Access-Control-Allow-Credentials", "true");
                res.Headers.Set("Vary", "Origin");
            }
            else
            {
                res.Headers.Set("Vary", "Origin");
            }

            res.Headers.Set("Access-Control-Allow-Methods", DefaultAllowedMethods);
            res.Headers.Set("Access-Control-Allow-Headers", DefaultAllowedHeaders);
        }

        public static void WritePreflight(
            RimApiContext ctx,
            IEnumerable<string> methods = null,
            IEnumerable<string> extraHeaders = null,
            ISet<string> allowedOrigins = null
        )
        {
            LogApi.Info("WritePreflight");
            var req = ctx.Request;
            var res = ctx.Response;
            var origin = req.Headers["Origin"];

            res.Headers.Set(
                "Access-Control-Allow-Methods",
                methods != null ? string.Join(", ", methods) : DefaultAllowedMethods
            );

            var requested = req.Headers["Access-Control-Request-Headers"];
            res.Headers.Set(
                "Access-Control-Allow-Headers",
                !string.IsNullOrEmpty(requested)
                    ? requested
                    : (extraHeaders != null ? string.Join(", ", extraHeaders) : DefaultAllowedHeaders)
            );

            if (string.IsNullOrEmpty(origin))
            {
                res.Headers.Set("Access-Control-Allow-Origin", "*");
            }
            else if (IsAllowedLocalOrigin(origin, allowedOrigins))
            {
                res.Headers.Set("Access-Control-Allow-Origin", origin);
                res.Headers.Set("Access-Control-Allow-Credentials", "true");
                res.Headers.Set("Vary", "Origin");
            }
            else
            {
                res.Headers.Set("Vary", "Origin");
                res.StatusCode = (int)HttpStatusCode.Forbidden;
                res.StatusDescription = "Forbidden";
                res.Close();
                return;
            }

            res.Headers.Set("Access-Control-Max-Age", "86400"); // Cache preflight for 24 hours
            res.StatusCode = (int)HttpStatusCode.NoContent; // 204 No Content
            res.StatusDescription = "No Content";
            res.Close();
        }
    }
}