using System;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Text;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Web;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Net.Http.Headers;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using IPA.Cores.Basic;

namespace IPA.Cores.Helper.Basic
{
    static class HelperWeb
    {
        public static Task SendStringContents(this HttpResponse h, string body, string contents_type = "text/plain; charset=UTF-8", Encoding encoding = null, CancellationToken cancel = default(CancellationToken))
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            h.ContentType = contents_type;
            byte[] ret_data = encoding.GetBytes(body);
            return h.Body.WriteAsync(ret_data, 0, ret_data.Length, cancel);
        }

        public static async Task<string> RecvStringContents(this HttpRequest h, int max_request_body_len = int.MaxValue, Encoding encoding = null, CancellationToken cancel = default(CancellationToken))
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            return (await h.Body.ReadToEndAsync(max_request_body_len, cancel)).GetString_UTF8();
        }
    }
}

