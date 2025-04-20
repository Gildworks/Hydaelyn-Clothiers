module CameraController

open System
open System.Numerics

type CameraController() as this =
    let mutable position = Vector3(0.0f, 0.0f, 20.0f)
    let mutable target = Vector3.Zero
    let mutable up = -Vector3.UnitY

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
        let pitch = orbitAngles.Y
        let yaw = orbitAngles.X

        let dir =
            Vector3(
                cos pitch * sin yaw,
                sin pitch,
                cos pitch * cos yaw
            )

        position <- target + dir * distance

    member _.MouseMove(current: Vector2) =
        let delta = current - lastMouse

        if isOrbiting then
            orbitAngles <- orbitAngles + delta * 0.01f
            this.UpdatePosition()
        elif isPanning then
            let forward = Vector3.Normalize(target - position)
            let right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward))
            let up = Vector3.Normalize(Vector3.Cross(forward, right))

            let panSpeed = distance * 0.002f
            target <- target - (delta.X * panSpeed * right) + (delta.Y * panSpeed * up)
            this.UpdatePosition()
        elif isDollying then
            distance <- max 2.0f (distance - delta.Y * 0.1f)
            this.UpdatePosition()

        if isOrbiting || isPanning || isDollying then
            lastMouse <- current

    member _.GetViewMatrix() : Matrix4x4 =
        Matrix4x4.CreateLookAt(position, target, up)

    member _.GetProjectionMatrix(aspectRatio: float32) : Matrix4x4 =
        Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            aspectRatio,
            1.0f,
            1000.0f
        )