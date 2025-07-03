namespace fs_mdl_viewer

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Text.Json
open System.Web


open Avalonia.Controls
open Avalonia.Markup.Xaml
open Avalonia.Platform.Storage

open Shared

type SettingsWindow() as this =
    inherit Window()

    do
        AvaloniaXamlLoader.Load(this)

    

    member private this.WaitForCallback() =
        async {
            use listener = new System.Net.HttpListener()
            listener.Prefixes.Add("http://localhost:8080/")
            listener.Start()

            let! context = listener.GetContextAsync() |> Async.AwaitTask
            let query = context.Request.Url.Query

            let queryParams = HttpUtility.ParseQueryString(query)
            let code = queryParams.["code"]

            let response = context.Response
            let responseString = "<html><body><h1>Authorization successful! You can close this window.</h1></body></html>"
            let buffer = System.Text.Encoding.UTF8.GetBytes(responseString)
            response.ContentLength64 <- int64 buffer.Length
            use output = response.OutputStream
            output.Write(buffer, 0, buffer.Length)

            listener.Stop()
            return code
        }

    member private this.ExchangeCodeForToken(code: string, clientId: string) =
        async {
            use client = new HttpClient()

            let tokenUrl = "https://www.patreon.com/api/oauth2/token"
            let content = new FormUrlEncodedContent([
                (Collections.Generic.KeyValuePair("grant_type", "authorization_code"))
                (Collections.Generic.KeyValuePair("code", code))
                (Collections.Generic.KeyValuePair("redirect_uri", "http://localhost:8080/callback"))
                (Collections.Generic.KeyValuePair("client_id", clientId))
                (Collections.Generic.KeyValuePair("client_secret", "your_client_secret"))
            ])

            let! response = client.PostAsync(tokenUrl, content) |> Async.AwaitTask
            let! json = response.Content.ReadAsStringAsync() |> Async.AwaitTask

            let tokenData = JsonSerializer.Deserialize<{| access_token: string |}>(json)
            return tokenData.access_token
        }

    member private this.GetPatreonId(accessToken: string) =
        async {
            use client = new HttpClient()
            client.DefaultRequestHeaders.Authorization <-
                System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken)

            let! response = client.GetAsync("https://www.patreon.com/api/oauth2/v2/identity") |> Async.AwaitTask
            let! json = response.Content.ReadAsStringAsync() |> Async.AwaitTask

            let userData = JsonSerializer.Deserialize<{| data: {| id: string |} |}>(json)
            return userData.data.id
        }
        