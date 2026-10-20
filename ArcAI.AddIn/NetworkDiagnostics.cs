using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace ArcAI.AddIn
{
    /// <summary>
    /// Diagnostics for "works on my machine" API connection failures:
    /// URL, proxy, DNS, TCP, TLS certificate and unauthenticated HTTP reachability.
    /// It never sends the API key.
    /// </summary>
    internal static class NetworkDiagnostics
    {
        public static async Task<string> RunAsync(string baseUrl)
        {
            return await Task.Run(() => Run(baseUrl)).ConfigureAwait(false);
        }

        private static string Run(string baseUrl)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ArcAI 网络诊断");
            sb.AppendLine("时间：" + DateTime.Now.ToString("s"));
            sb.AppendLine("系统：" + Environment.OSVersion);
            sb.AppendLine(".NET：" + Environment.Version);
            sb.AppendLine();

            string endpoint;
            try
            {
                endpoint = LlmClient.NormalizeEndpoint(baseUrl);
            }
            catch (Exception ex)
            {
                sb.AppendLine("Base URL 错误：" + ex.Message);
                return sb.ToString();
            }
            Uri uri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri)
                || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                sb.AppendLine("Endpoint 不是有效的 http/https 地址：" + endpoint);
                return sb.ToString();
            }

            sb.AppendLine("Endpoint：" + endpoint);
            sb.AppendLine("Host：" + uri.Host + "   Port：" + uri.Port);
            AppendProxy(sb, uri);
            AppendEnvironmentProxy(sb);

            bool dnsOk = false;
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(uri.Host);
                dnsOk = addresses != null && addresses.Length > 0;
                sb.AppendLine("DNS：" + (dnsOk ? Join(addresses) : "无结果"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("DNS 失败：" + Flatten(ex));
            }

            bool tcpOk = false;
            if (dnsOk)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        Task connect = client.ConnectAsync(uri.Host, uri.Port);
                        if (!connect.Wait(6000))
                        {
                            sb.AppendLine("TCP：连接超时（6 秒）");
                        }
                        else
                        {
                            tcpOk = client.Connected;
                            sb.AppendLine("TCP：" + (tcpOk ? "连接成功" : "未连接"));
                            if (tcpOk)
                                AppendTls(sb, client, uri);
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("TCP 失败：" + Flatten(ex));
                }
            }
            else
            {
                sb.AppendLine("TCP：跳过（DNS 未解析）");
            }

            AppendHttp(sb, uri, tcpOk);
            sb.AppendLine();
            sb.AppendLine("说明：");
            sb.AppendLine("- HTTP 401/403/405：网络已连通，重点检查 API Key、Base URL、模型权限。");
            sb.AppendLine("- HTTP 407：需要代理认证，请检查 Windows 代理/凭据。");
            sb.AppendLine("- DNS/TCP/TLS 失败：检查网络、防火墙、代理、VPN 或证书。");
            sb.AppendLine("- 证书错误：安装企业根证书，或临时取消“验证 SSL 证书”。");
            return sb.ToString();
        }

        private static void AppendProxy(StringBuilder sb, Uri uri)
        {
            try
            {
                IWebProxy proxy = WebRequest.GetSystemWebProxy();
                if (proxy == null)
                {
                    sb.AppendLine("系统代理：未配置");
                    return;
                }
                bool bypass = proxy.IsBypassed(uri);
                Uri proxyUri = bypass ? null : proxy.GetProxy(uri);
                sb.AppendLine("系统代理：" + (bypass ? "绕过（直连）"
                    : (proxyUri == null ? "未配置" : proxyUri.ToString())));
            }
            catch (Exception ex)
            {
                sb.AppendLine("系统代理读取失败：" + Flatten(ex));
            }
        }

        private static void AppendEnvironmentProxy(StringBuilder sb)
        {
            string[] names = { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY" };
            foreach (string name in names)
            {
                string value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value))
                    sb.AppendLine(name + "：" + value);
            }
        }

        private static void AppendTls(StringBuilder sb, TcpClient client, Uri uri)
        {
            try
            {
                string subject = string.Empty;
                string issuer = string.Empty;
                string expires = string.Empty;
                SslPolicyErrors errors = SslPolicyErrors.None;
                using (var ssl = new SslStream(client.GetStream(), false,
                    delegate(object sender, X509Certificate certificate, X509Chain chain,
                        SslPolicyErrors policyErrors)
                    {
                        errors = policyErrors;
                        if (certificate != null)
                        {
                            subject = certificate.Subject;
                            issuer = certificate.Issuer;
                            expires = certificate.GetExpirationDateString();
                        }
                        return true;
                    }))
                {
                    ssl.AuthenticateAsClient(uri.Host);
                    sb.AppendLine("TLS：" + ssl.SslProtocol + "，证书验证："
                        + (errors == SslPolicyErrors.None ? "通过" : errors.ToString()));
                    if (!string.IsNullOrEmpty(subject))
                        sb.AppendLine("证书 Subject：" + subject);
                    if (!string.IsNullOrEmpty(issuer))
                        sb.AppendLine("证书 Issuer：" + issuer);
                    if (!string.IsNullOrEmpty(expires))
                        sb.AppendLine("证书到期：" + expires);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("TLS 失败：" + Flatten(ex));
            }
        }

        private static void AppendHttp(StringBuilder sb, Uri uri, bool tcpOk)
        {
            if (!tcpOk)
            {
                sb.AppendLine("HTTP：跳过（TCP 未连通）");
                return;
            }
            try
            {
                using (var handler = new HttpClientHandler())
                {
                    IWebProxy proxy = WebRequest.GetSystemWebProxy();
                    if (proxy != null)
                    {
                        proxy.Credentials = CredentialCache.DefaultCredentials;
                        handler.Proxy = proxy;
                        handler.UseProxy = true;
                    }
                    using (var client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(15);
                        using (var request = new HttpRequestMessage(HttpMethod.Head, uri))
                        {
                            using (HttpResponseMessage response = client.SendAsync(request)
                                .GetAwaiter().GetResult())
                            {
                                sb.AppendLine("HTTP HEAD：" + (int)response.StatusCode
                                    + " " + response.ReasonPhrase + "（未携带 API Key）");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("HTTP HEAD 失败：" + Flatten(ex));
            }
        }

        private static string Join(IPAddress[] addresses)
        {
            var sb = new StringBuilder();
            foreach (IPAddress address in addresses)
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(address);
            }
            return sb.ToString();
        }

        private static string Flatten(Exception ex)
        {
            var sb = new StringBuilder();
            Exception current = ex;
            while (current != null)
            {
                if (sb.Length > 0)
                    sb.Append(" → ");
                sb.Append(current.GetType().Name + ": " + current.Message);
                current = current.InnerException;
            }
            return sb.ToString();
        }
    }
}
