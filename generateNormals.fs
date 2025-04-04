module generateNormals

open System.Numerics

let generateNormals (positions: Vector3[]) (indices: uint16[]) : Vector3[] =
    let normals = Array.init positions.Length (fun _ -> Vector3.Zero)

    for i in 0 .. 3 .. (indices.Length - 3) do
        let i0 = int indices[i]
        let i1 = int indices[i + 1]
        let i2 = int indices[i + 2]

        let v0 = positions[i0]
        let v1 = positions[i1]
        let v2 = positions[i2]

        let edge1 = v1 - v0
        let edge2 = v2 - v0
        let faceNormal = Vector3.Normalize(Vector3.Cross(edge1, edge2))

        normals[i0] <- normals[i0] + faceNormal
        normals[i1] <- normals[i1] + faceNormal
        normals[i2] <- normals[i2] + faceNormal

    normals
    |> Array.map (fun n ->
        if n = Vector3.Zero then Vector3.UnitZ
        else Vector3.Normalize(n)
    )