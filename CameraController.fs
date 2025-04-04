module CameraController

open System
open System.Numerics

type CameraController() as this =
    let mutable position = Vector3(0.0f, 0.0f, -20.f)
    let mutable target = Vector3.Zero
    let mutable up = Vector3.UnitX

    let mutable orbitAngles = Vector2(0.0f, 0.0f)
    let mutable distance = 20.0f
    let mutable isOrbiting = false
    let mutable isPanning = false
    let mutable isDollying = false
    let mutable lastMouse = Vector2.Zero

    member _.Position with get() = position
    member _.Target with get() = target
    member _.Up = up
    member _.ViewMatrix = Matrix4x4.CreateLookAt(position, target, up)

    member _.StartOrbit(mouse: Vector2) =
        isOrbiting <- true
        lastMouse <- mouse

    member _.StartPan(mouse: Vector2) =
        isPanning <- true
        lastMouse <- mouse

    member _.StartDolly(mouse: Vector2) =
        isDollying <- true
        lastMouse <- mouse

    member _.Stop() =
        isOrbiting <- false
        isPanning <- false
        isDollying <- false

    member _.Zoom(delta: float32) =
        distance <- max 2.0f (distance - delta)
        this.UpdatePosition()

    member private _.UpdatePosition() =
        let pitch = orbitAngles.X
        let yaw = orbitAngles.Y

        let x = distance * cos pitch * sin yaw
        let y = distance * sin pitch
        let z = distance * cos pitch * cos yaw

        position <- Vector3(x, y, z) + target

    member _.MouseMove(current: Vector2) =
        let delta = current - lastMouse
        lastMouse <- current

        if isOrbiting then
            orbitAngles <- orbitAngles + delta * 0.01f
            this.UpdatePosition()
        elif isPanning then
            let forward = Vector3.Normalize(target - position)
            let right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward))
            let upMove = Vector3.Normalize(Vector3.Cross(forward, right))
            target <- target - (right * delta.X * 0.05f) + (upMove * delta.Y * 0.05f)
            this.UpdatePosition()
        elif isDollying then
            distance <- max 2.0f (distance - delta.Y * 0.1f)
            this.UpdatePosition()

    member _.GetViewMatrix() : Matrix4x4 =
        Matrix4x4.CreateLookAt(position, target, up)

    member _.GetProjectionMatrix(aspectRatio: float32) : Matrix4x4 =
        Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            aspectRatio,
            0.1f,
            1000.0f
        )