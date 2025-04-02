module GameData

open Lumina
open System.IO

let initializeGameData (path: string) =
    if not (Directory.Exists path) then
        failwithf "Directory does not exist: %s" path
    try
        let lumina = new Lumina.GameData(path)
        printfn "Lumina initialized successfully with path %s" path
        Some lumina
    with
    | ex ->
        printfn "Failed to load game data: %s" ex.Message
        None