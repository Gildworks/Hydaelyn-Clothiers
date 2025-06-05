module CameraController

open System
open System.Numerics

type CameraController(?initialDistance: float32, ?initialYaw: float32, ?initialPitch: float32, ?initialTargetYPercentageFromBottom: float32) as this =
    // Core orbit parameters
    let mutable target = Vector3.Zero
    let mutable orbitAngles = Vector2(Option.defaultValue 0.0f initialYaw, Option.defaultValue 0.0f initialPitch) // X: Yaw, Y: Pitch
    let mutable distance = Option.defaultValue 20.0f initialDistance

    // Calculated camera state
    let mutable position = Vector3.Zero
    let mutable up = Vector3.UnitY

    let mutable isOrbiting = false
    let mutable isPanning = false
    let mutable isDollying = false
    let mutable lastMouse = Vector2.Zero

    do
        let fovY = MathF.PI / 6.0f // From your GetProjectionMatrix
        let P = Option.defaultValue 0.5f initialTargetYPercentageFromBottom // Default to center (0,0,0) if not specified

        if P < 0.0f || P > 1.0f then
            raise (ArgumentOutOfRangeException("initialTargetYPercentageFromBottom must be between 0.0 and 1.0"))

        let H_world = 2.0f * distance * MathF.Tan(fovY / 2.0f)
        let calculatedTargetY = H_world * (0.5f - P)
        target <- Vector3(0.0f, calculatedTargetY, 0.0f)

        this.RecalculateCameraState() // Initialize position and up based on new target and other params

    // Private method to update camera's position and its dynamic "up" vector
    member private this.RecalculateCameraState() =
        let R = distance
        let yaw = orbitAngles.X
        let pitch = orbitAngles.Y

        let cosPitch = MathF.Cos(pitch)
        let sinPitch = MathF.Sin(pitch)
        let cosYaw = MathF.Cos(yaw)
        let sinYaw = MathF.Sin(yaw)

        let dirToCamera = Vector3(R * cosPitch * sinYaw, R * sinPitch, R * cosPitch * cosYaw)
        position <- target + dirToCamera

        let naturalSphereUpRaw = Vector3(-R * sinPitch * sinYaw, R * cosPitch, -R * sinPitch * cosYaw)
        let correctedUpVecRaw = -naturalSphereUpRaw // Your fix for model orientation

        if correctedUpVecRaw.LengthSquared() < 0.000001f then
            // Fallback logic (ensure 'up' is valid and consistent with desired orientation)
            let viewDirNorm = if (target - position).LengthSquared() > 0.000001f then Vector3.Normalize(target - position) else -Vector3.UnitZ
            let tempRight = Vector3.Cross(viewDirNorm, Vector3.UnitY)
            let fallbackUpCandidate =
                if tempRight.LengthSquared() < 0.000001f then
                    Vector3.Cross(viewDirNorm, Vector3.UnitX)
                else
                    Vector3.Cross(Vector3.Normalize(tempRight), viewDirNorm)

            up <- if fallbackUpCandidate.LengthSquared() < 0.000001f then -Vector3.UnitY else Vector3.Normalize(fallbackUpCandidate)
            // Ensure the fallback 'up' is consistent with the 'correctedUpVecRaw' general direction if possible,
            // or at least a sane default like -Vector3.UnitY if that matches "right side up".
            if Vector3.Dot(up, -Vector3.UnitY) < 0.0f && (pitch > -MathF.PI/2.0f && pitch < MathF.PI/2.0f) then // Heuristic: if not near poles and up is wrong way
                 up <- -up // Attempt to flip if it seems oriented opposite to -UnitY for standard views
            if up.LengthSquared() < 0.000001f then up <- -Vector3.UnitY // Final absolute fallback
            
        else
            up <- Vector3.Normalize(correctedUpVecRaw)

    // Initialize camera state
   

    member _.Position with get() = position
    member _.Target with get() = target
    member _.Up with get() = up
    member _.ViewMatrix = Matrix4x4.CreateLookAt(position, target, up)

    member _.StartOrbit(mouse: Vector2) = isOrbiting <- true; lastMouse <- mouse
    member _.StartPan(mouse: Vector2) = isPanning <- true; lastMouse <- mouse
    member _.StartDolly(mouse: Vector2) = isDollying <- true; lastMouse <- mouse

    member _.Stop() = isOrbiting <- false; isPanning <- false; isDollying <- false

    member _.Zoom(delta: float32) =
        distance <- max 0.1f (distance - delta) // Ensure distance doesn't become zero or negative
        this.RecalculateCameraState()

    member _.MouseMove(current: Vector2) =
        let delta = current - lastMouse
        let SENSITIVITY = 0.008f // Adjusted sensitivity

        if isOrbiting then
            orbitAngles <- Vector2(orbitAngles.X + delta.X * SENSITIVITY, orbitAngles.Y + delta.Y * SENSITIVITY)
            this.RecalculateCameraState()
        elif isPanning then
            let panSpeed = distance * 0.001f // Adjusted pan speed for smoother control

            let zAxisLocal = if (position - target).LengthSquared() > 0.000001f then Vector3.Normalize(position - target) else Vector3.UnitZ
            let cameraActualRight = if Vector3.Cross(this.Up, zAxisLocal).LengthSquared() > 0.000001f then Vector3.Normalize(Vector3.Cross(this.Up, zAxisLocal)) else Vector3.UnitX
            let cameraActualUp = if Vector3.Cross(zAxisLocal, cameraActualRight).LengthSquared() > 0.000001f then Vector3.Normalize(Vector3.Cross(zAxisLocal, cameraActualRight)) else Vector3.UnitY
            
            target <- target - (delta.X * panSpeed * cameraActualRight) - (delta.Y * panSpeed * cameraActualUp) // Your Y-pan fix
            this.RecalculateCameraState()
        elif isDollying then
            let dollySpeed = 0.05f // Adjusted dolly speed
            distance <- max 0.1f (distance - delta.Y * dollySpeed * distance) // Make dolly speed proportional to distance
            this.RecalculateCameraState()

        if isOrbiting || isPanning || isDollying then
            lastMouse <- current

    member _.GetViewMatrix() : Matrix4x4 = Matrix4x4.CreateLookAt(position, target, up)

    member _.GetProjectionMatrix(aspectRatio: float32) : Matrix4x4 =
        Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 6.0f, // fovY
            aspectRatio,
            0.1f,            // Near plane
            1000.0f          // Far plane
        )