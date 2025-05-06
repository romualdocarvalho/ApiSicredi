using Negocios.Enums;
using Negocios.Static;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace BancoSicredi
{
    public class BancoSicrediApi
    {
        private string AccessToken = "";
        private string RefreshToken = "";

        private string UrlToken {
            get => AmbienteProducao
                ? "https://api-parceiro.sicredi.com.br/auth/openapi/token"
                : "https://api-parceiro.sicredi.com.br/sb/auth/openapi/token";
        }

        private string UrlBoletosLiquidadosPorDia
        {
            get => AmbienteProducao
                ? "https://api-parceiro.sicredi.com.br/cobranca/boleto/v1/boletos/liquidados/dia"
                : "https://api-parceiro.sicredi.com.br/sb/cobranca/boleto/v1/boletos/liquidados/dia";
        }

        private LogDB Logger;
        private string ApiKey;
        private string Username;
        private string Password;
        private string Cooperativa;
        private string CodigoBeneficiario;
        private string Posto;
        private bool AmbienteProducao = false;

        public string GetLogString()
        {
            return (
                $"{nameof(ApiKey)}={"".PadLeft(ApiKey?.Length ?? 0, '*')}, " +
                $"{nameof(Username)}={Username}, " +
                $"{nameof(Password)}={"".PadLeft(Password?.Length ?? 0, '*')}, " +
                $"{nameof(Cooperativa)}={Cooperativa}, " +
                $"{nameof(CodigoBeneficiario)}={CodigoBeneficiario}, " +
                $"{nameof(Posto)}={Posto}, " +
                $"{nameof(AmbienteProducao)}={AmbienteProducao}, "
            );
        }

        public bool TodosParametrosPreenchidos()
        {
            return (
                !string.IsNullOrEmpty(ApiKey) &&
                !string.IsNullOrEmpty(Username) &&
                !string.IsNullOrEmpty(Password) &&
                !string.IsNullOrEmpty(Cooperativa) &&
                !string.IsNullOrEmpty(CodigoBeneficiario) &&
                !string.IsNullOrEmpty(Posto)
            );
        }

        public BancoSicrediApi(string apiKey, string username, string password, bool ambienteProducao, string cooperativa, string codigoBeneficiario, string posto, LogDB logger)
        {
            Logger = logger;
            ApiKey = apiKey;
            Username = username;
            Password = password;
            AmbienteProducao = ambienteProducao;
            Cooperativa = cooperativa;
            CodigoBeneficiario = codigoBeneficiario;
            Posto = posto;
        }

        public async Task<List<BancoSicrediApiDTO_BoletosLiquidadosPorDia_Item>> BuscaBoletosLiquidadosPorDia(DateTime data)
        {
            int pagina = 0;

            if (string.IsNullOrEmpty(AccessToken))
            {
                await AtualizaTokenAsync(GrantType.Password);
            }

            var boletos = new List<BancoSicrediApiDTO_BoletosLiquidadosPorDia_Item>();

            while (true)
            {
                var infoBoletos = await BuscaBoletosLiquidadosPorDiaPaginado(data, pagina);

                if (infoBoletos.instrucao == InstrucaoConsulta.RefreshToken)
                {
                    Logger.Info(TipoOperacaoLog.Log, "Token expirado, consultando novo");

                    if (!await AtualizaTokenAsync(GrantType.RefreshToken))
                    {
                        await AtualizaTokenAsync(GrantType.Password);
                    }

                    infoBoletos = await BuscaBoletosLiquidadosPorDiaPaginado(data, pagina);
                }

                if (infoBoletos.instrucao == InstrucaoConsulta.Esperar)
                {
                    const int SEGUNDOS_ESPERAR = 30;
                    Logger.Info(TipoOperacaoLog.Log, $"Muitas requisições, aguardando {SEGUNDOS_ESPERAR}s");
                    Thread.Sleep(1000 * SEGUNDOS_ESPERAR);
                    continue;
                }

                if (infoBoletos.instrucao == InstrucaoConsulta.ProximaPagina)
                {
                    boletos = boletos.Concat(infoBoletos.items).ToList();
                    pagina++;
                    Logger.Info(TipoOperacaoLog.Log, $"Consultando próxima página ({pagina})");
                    continue;
                }

                if (infoBoletos.instrucao == InstrucaoConsulta.Concluido)
                {
                    boletos = boletos.Concat(infoBoletos.items).ToList();
                    break;
                }

                if (infoBoletos.instrucao == InstrucaoConsulta.RefreshToken)
                {
                    break;
                }
            }

            return boletos;
        }

        private async Task<(InstrucaoConsulta instrucao, List<BancoSicrediApiDTO_BoletosLiquidadosPorDia_Item> items)> BuscaBoletosLiquidadosPorDiaPaginado(DateTime data, int pagina)
        {
            var queryParams = HttpUtility.ParseQueryString("");

            queryParams.Add("codigoBeneficiario", CodigoBeneficiario);
            queryParams.Add("dia", data.ToString("dd/MM/yyyy"));
            //queryParams.Add("cpfCnpjBeneficiarioFinal", CpfCnpjBeneficiarioFinal);
            queryParams.Add("pagina", pagina.ToString());

            var url = UrlBoletosLiquidadosPorDia + '?' + queryParams.ToString();

            Logger.Info(TipoOperacaoLog.Log, $"Requisitando: {url}, {nameof(AmbienteProducao)}={AmbienteProducao}");

            using (var handler = new HttpClientHandler())
            using (var httpClient = new HttpClient(handler))
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                handler.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;

                request.Headers.Add("x-api-key", ApiKey);
                request.Headers.Add("authorization", $"Bearer {AccessToken}");
                request.Headers.Add("cooperativa", Cooperativa);
                request.Headers.Add("posto", Posto);

                var response = await httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        return (InstrucaoConsulta.RefreshToken, null);
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        return (InstrucaoConsulta.Esperar, null);
                    }

                    throw new Exception($"{nameof(BuscaBoletosLiquidadosPorDiaPaginado)}: {(int)response.StatusCode}, {response.ReasonPhrase}, {queryParams}");
                }

                if (!response.Content.Headers.ContentType.MediaType.Contains("application/json"))
                {
                    throw new Exception($"{nameof(BuscaBoletosLiquidadosPorDiaPaginado)}: {response.Content.Headers.ContentType.MediaType}, esperado JSON, {queryParams}");
                }

                var objRetorno = JsonConvert.DeserializeObject<BancoSicrediApiDTO_BoletosLiquidadosPorDia>(responseString);

                return (
                    instrucao: objRetorno.hasNext ? InstrucaoConsulta.ProximaPagina : InstrucaoConsulta.Concluido,
                    items: objRetorno.items
                );
            }
        }

        private async Task<bool> AtualizaTokenAsync(GrantType grantType)
        {
            using (var handler = new HttpClientHandler())
            using (var httpClient = new HttpClient(handler))
            using (var request = new HttpRequestMessage(HttpMethod.Post, UrlToken))
            {
                handler.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;

                request.Headers.Add("x-api-key", ApiKey);
                request.Headers.Add("context", "COBRANCA");

                var collection = new Dictionary<string, string>
                {
                    { "scope", "cobranca" },
                };

                switch (grantType)
                {
                    case GrantType.Password:
                        collection.Add("grant_type", "password");
                        collection.Add("username", Username);
                        collection.Add("password", Password);
                        break;

                    case GrantType.RefreshToken:
                        collection.Add("grant_type", "refresh_token");
                        collection.Add("refresh_token", RefreshToken);
                        break;

                    default:
                        throw new NotImplementedException($"{nameof(AtualizaTokenAsync)}:{nameof(GrantType)} {grantType} não implementado!");
                }

                request.Content = new FormUrlEncodedContent(collection);

                var response = await httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (grantType == GrantType.RefreshToken)
                    {
                        return false;
                    }

                    throw new Exception($"{nameof(AtualizaTokenAsync)}: {nameof(response.StatusCode)}={response.StatusCode}, {nameof(response.ReasonPhrase)}={response.ReasonPhrase}, {nameof(responseString)}={responseString}");
                }

                if (response.Content.Headers.ContentType.MediaType.Contains("application/json"))
                {
                    dynamic jsonObject = JObject.Parse(responseString);

                    //{
                    //    "access_token": "eyJhbGciOiJSUzI1NiIsInR5cCIgOiAiSldUIiwia2lkIiA6ICJxMlRFLXliYWxnNjIxSmRCMU9IX0w2Sms5QllxODJtNkFSZ0NGaDZtQnZBIn0.eyJleHAiOjE3MTg4MjI5ODgsImlhdCI6MTcxODgyMjY4OCwianRpIjoiZjY4OGYxNzgtNzU0Ny00N2MzLWFkMGEtNmQ3N2QxNTlhMWI5IiwiaXNzIjoiaHR0cHM6Ly9hdXRoLW9wZW5hcGkucHJkLnNpY3JlZGkuY2xvdWQvcmVhbG1zL29wZW5hcGkiLCJhdWQiOiJhY2NvdW50Iiwic3ViIjoiZjozMWY3ODE1MS1jMzdmLTQzOWYtYTIwOS02NjFjYTQ3MzkzYTE6Q09CUkFOQ0E6MTI4MTcwNzQwIiwidHlwIjoiQmVhcmVyIiwiYXpwIjoib3BlbmFwaS1ndy1zZW5zZWRpYSIsInNlc3Npb25fc3RhdGUiOiI2ODg2YmFjOC02MjQzLTRkZjctYTZiNS1iOGMxZTc3ZTg0N2EiLCJhY3IiOiIxIiwicmVhbG1fYWNjZXNzIjp7InJvbGVzIjpbImRlZmF1bHQtcm9sZXMtb3BlbmFwaSIsIm9mZmxpbmVfYWNjZXNzIiwidW1hX2F1dGhvcml6YXRpb24iXX0sInJlc291cmNlX2FjY2VzcyI6eyJhY2NvdW50Ijp7InJvbGVzIjpbIm1hbmFnZS1hY2NvdW50IiwibWFuYWdlLWFjY291bnQtbGlua3MiLCJ2aWV3LXByb2ZpbGUiXX19LCJzY29wZSI6ImNvYnJhbmNhIHByb2ZpbGUgZW1haWwiLCJzaWQiOiI2ODg2YmFjOC02MjQzLTRkZjctYTZiNS1iOGMxZTc3ZTg0N2EiLCJvcGVuYXBpX3VzZXJuYW1lIjoiMTI4MTcwNzQwIiwib3BlbmFwaV9jb250ZXh0IjoiQ09CUkFOQ0EiLCJlbWFpbF92ZXJpZmllZCI6ZmFsc2UsInByZWZlcnJlZF91c2VybmFtZSI6IjEyODE3MDc0MCJ9.IfTI2uN9-xo_mI4RJdG4XmeLFT2vrMg1GjCYQjUFxoE_xfGUyZ8Kwq6r8yB2zK3bFt5nUPm7PEihRuu4kih7UEgFr1IRFfpMSAYC7krvG6Ih_sSkpw--F650q1Van-Wtl2_28UIlcpfed-BX6A2u_qbb9I5LGqtJ5_-vCy_9WkAlQEc0Q2-30bYjvRo4KNYLkel1RTsFv5tIjdV9lf3YXSNDezXnwxCnFS7CgxRZMQVOw_-9nBH4eUrc8c77VZmdAXwudVYN1NuJ4K7jhQvs1XsOxJYhvNDJTOfkm8rozo2q5Q2LGhyTv-pqDEU3xZSndEpnHRJTtxdMC6z1eaOWBg",
                    //    "expires_in": 300,
                    //    "refresh_expires_in": 1800,
                    //    "refresh_token": "eyJhbGciOiJIUzI1NiIsInR5cCIgOiAiSldUIiwia2lkIiA6ICJhMTRlOTdmOC1lZmJiLTQ2YTYtOTYwYy0wNTFlZjA4MTU3MjEifQ.eyJleHAiOjE3MTg4MjQ0ODgsImlhdCI6MTcxODgyMjY4OCwianRpIjoiZWIwMGExNTktNzI3ZC00MmMxLWJjYWYtMjIyNzAxODg4MzNlIiwiaXNzIjoiaHR0cHM6Ly9hdXRoLW9wZW5hcGkucHJkLnNpY3JlZGkuY2xvdWQvcmVhbG1zL29wZW5hcGkiLCJhdWQiOiJodHRwczovL2F1dGgtb3BlbmFwaS5wcmQuc2ljcmVkaS5jbG91ZC9yZWFsbXMvb3BlbmFwaSIsInN1YiI6ImY6MzFmNzgxNTEtYzM3Zi00MzlmLWEyMDktNjYxY2E0NzM5M2ExOkNPQlJBTkNBOjEyODE3MDc0MCIsInR5cCI6IlJlZnJlc2giLCJhenAiOiJvcGVuYXBpLWd3LXNlbnNlZGlhIiwic2Vzc2lvbl9zdGF0ZSI6IjY4ODZiYWM4LTYyNDMtNGRmNy1hNmI1LWI4YzFlNzdlODQ3YSIsInNjb3BlIjoiY29icmFuY2EgcHJvZmlsZSBlbWFpbCIsInNpZCI6IjY4ODZiYWM4LTYyNDMtNGRmNy1hNmI1LWI4YzFlNzdlODQ3YSJ9.1JLfywbqKKTal5g1L69R5uTLiQbwbYHqjR3UcyoZNvs",
                    //    "token_type": "Bearer",
                    //    "not-before-policy": 0,
                    //    "session_state": "6886bac8-6243-4df7-a6b5-b8c1e77e847a",
                    //    "scope": "cobranca profile email"
                    //}

                    string access_token = jsonObject.access_token ?? "";
                    string refresh_token = jsonObject.refresh_token ?? "";

                    if (string.IsNullOrEmpty(access_token))
                    {
                        throw new Exception($"{nameof(AtualizaTokenAsync)}: Não retornou 'access_token' {nameof(responseString)}={responseString}");
                    }

                    AccessToken = access_token;
                    RefreshToken = refresh_token;

                    return true;
                }

                throw new Exception($"{nameof(AtualizaTokenAsync)}: Retorno não é JSON {nameof(response.Content.Headers.ContentType.MediaType)}={response.Content.Headers.ContentType.MediaType}");
            }
        }

        private enum GrantType
        {
            Password,
            RefreshToken
        }

        private enum InstrucaoConsulta
        {
            Concluido,
            RefreshToken,
            ProximaPagina,
            Esperar
        }
    }
}
