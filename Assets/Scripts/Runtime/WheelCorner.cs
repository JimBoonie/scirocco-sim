using UnityEngine;
// One corner. Raycast wheel position -> wheel travel t (m, +bump, 0 = design ride height).
// Vertical: spring (wheel rate, preloaded to carry static load at t=0) + piecewise damper through the kinematic motion ratio
// + progressive bump stop + ARB/beam (from controller) + jacking force from the roll-centre height.
// Tire: Pacejka combined slip with relaxation length; lateral force applied at roll-centre height (geometric load transfer).
public class WheelCorner : MonoBehaviour
{
    public CarParams p; public bool front, left; public Transform visual;
    [HideInInspector] public float travel, Fz, Fspring, Fdamper, Fbump, Farb, Fjack, Fanti, steerDeg, driveTorque, brakeTorque, camberDeg, rcHeight;
    [HideInInspector] public float omega, slipRatio, slipAngle, alphaRelaxed, staticSag, halfTrack;
    [HideInInspector] public Vector2 force; [HideInInspector] public bool grounded;
    [HideInInspector] public Vector3 bodyForce;             // total force this corner puts into the body (world)
    float lastTravel, spinDeg; bool haveLast; RaycastHit hit; Rigidbody rb;
    public RaycastHit Hit => hit; public float WheelY => yw;   // diagnostics
    public float DriveInertia;
    void Awake() { rb = GetComponentInParent<Rigidbody>(); }
    float Bump => front ? p.bumpTravelF : p.bumpTravelR;
    float Rebound => front ? p.reboundTravelF : p.reboundTravelR;
    float MR(float t) { var k = p.kin; if (!k) return 1f; return SuspensionTables.S1(k.travel, front ? k.frontMotionRatio : k.rearMotionRatio, t); }
    public float RC(float t) { var k = p.kin; if (!k) return 0f; if (front) return SuspensionTables.S1(k.travel, k.frontRcHeight, t); return p.rearModel == CarParams.RearModel.FullTwistBeam ? k.rbRcHeight : k.rearRcHeight; }
    [HideInInspector] public float antiCoef;   // vertical body force per N of +x tire force (set by controller)
    float BumpStopForce(float x)   // integrate piecewise-linear rate table
    {
        if (x <= 0f) return 0f; float F = 0f; var X = p.bumpStopX; var K = p.bumpStopRate;
        for (int i = 1; i < X.Length; i++)
        {
            float x0 = X[i - 1], x1 = X[i]; if (x <= x0) break; float xe = Mathf.Min(x, x1);
            float k0 = K[i - 1], k1 = Mathf.Lerp(K[i - 1], K[i], (xe - x0) / (x1 - x0)); F += 0.5f * (k0 + k1) * (xe - x0);
        }
        if (x > X[X.Length - 1]) F += K[K.Length - 1] * (x - X[X.Length - 1]);
        return F;
    }
    float Damper(float v)
    {
        var V = p.damperV; var B = front ? p.damperBumpF : p.damperBumpR; var R = front ? p.damperReboundF : p.damperReboundR;
        float a = Mathf.Abs(v), f; int n = V.Length; var tab = v >= 0f ? B : R;
        if (a >= V[n - 1]) f = tab[n - 1] + (tab[n - 1] - tab[n - 2]) / (V[n - 1] - V[n - 2]) * (a - V[n - 1]);
        else f = SuspensionTables.S1(V, tab, a);
        return v >= 0f ? f : -f;
    }
    public float WheelRate => front ? p.wheelRateFront : p.wheelRateRear;
    // ---- unsprung vertical DOF (p.useUnsprungMass): wheel-centre height yw / velocity vw in world, tire spring to ground ----
    [HideInInspector] public float Ftire, Fsusp, unsprungMass; float yw, vw; bool wInit; Vector3 lastPtVel; bool havePt;
    public void UpdateSuspension(float dt, float arbForce)
    {
        if (!rb) rb = GetComponentInParent<Rigidbody>();
        bool um = p.useUnsprungMass; unsprungMass = um ? (front ? p.unsprungFront : p.unsprungRear) : 0f;
        float statDefl = um ? unsprungStaticLoad / p.tireRate : 0f;   // tire static deflection: free radius = R + statDefl keeps design ride height
        float rayLen = p.hubDropDesign + Rebound + p.wheelRadius + statDefl + 0.05f;
        grounded = Physics.Raycast(transform.position, -transform.up, out hit, rayLen);
        Vector3 hubDesign = transform.position - transform.up * p.hubDropDesign;
        float upY = Mathf.Max(transform.up.y, 0.3f);
        if (!um)
        {
            if (grounded && hit.distance > p.hubDropDesign + Rebound + p.wheelRadius) grounded = false;
            if (!grounded) { travel = -Rebound; haveLast = false; Fz = Ftire = Fsusp = Fspring = Fdamper = Fbump = Farb = Fjack = Fanti = 0f; rcHeight = RC(travel); bodyForce = Vector3.zero; return; }
            float tRaw = p.hubDropDesign - (hit.distance - p.wheelRadius);
            travel = Mathf.Clamp(tRaw, -Rebound, Bump);
            float v = haveLast ? (travel - lastTravel) / dt : 0f; lastTravel = travel; haveLast = true;
            Forces(v, tRaw, arbForce);
            Fz = Ftire = Mathf.Max(0f, Fsusp);
            Vector3 fv = hit.normal * Fz; rb.AddForceAtPosition(fv, transform.position); bodyForce = fv;
            return;
        }
        Vector3 cv = rb.GetPointVelocity(hubDesign);
        if (!wInit) { yw = hubDesign.y; vw = cv.y; wInit = true; }
        travel = (yw - hubDesign.y) / upY; float vrel = (vw - cv.y) / upY;
        Forces(vrel, travel, arbForce);
        float topOut = travel < -Rebound ? -2e5f * (-Rebound - travel) : 0f;   // damper top-out stop (E)
        Fsusp += topOut;
        // tire: deflection relative to the free radius
        if (grounded)
        {
            float defl = hit.point.y + p.wheelRadius + statDefl - yw;
            Ftire = defl > 0f ? Mathf.Max(0f, p.tireRate * defl - p.tireDamping * vw) : 0f;
            if (Ftire <= 0f) grounded = false;
        }
        else Ftire = 0f;
        Fz = Ftire;
        // semi-implicit wheel integration (1 kHz): m_u yw'' = Ftire - Fsusp*up.y - m_u g
        vw += ((Ftire - Fsusp * upY) / unsprungMass - 9.81f) * dt; yw += vw * dt;
        Vector3 fb = transform.up * Fsusp; rb.AddForceAtPosition(fb, transform.position);
        // unsprung horizontal inertia (wheels move with the body horizontally; body Rigidbody carries sprung mass only)
        Vector3 pv = rb.GetPointVelocity(hubDesign);
        if (havePt) { Vector3 acc = (pv - lastPtVel) / dt; acc.y = 0f; rb.AddForceAtPosition(-unsprungMass * acc, hubDesign); }
        lastPtVel = pv; havePt = true;
        bodyForce = fb;
    }
    [HideInInspector] public float unsprungStaticLoad;   // tire static load (sprung + unsprung corner weight), set by controller
    public void ResetState() { wInit = false; haveLast = false; havePt = false; vw = 0f; omega = 0f; alphaRelaxed = 0f; }   // after a teleport: re-seed the unsprung wheel height from the body (stale yw launched the car)
    void Forces(float v, float tRaw, float arbForce)
    {
        float mr = MR(Mathf.Clamp(travel, -Rebound, Bump));
        Fspring = WheelRate * (travel + staticSag);
        Fdamper = Damper(v * mr) * mr;
        Fbump = BumpStopForce(tRaw - p.bumpStopGap) + (tRaw > Bump ? 2e6f * (tRaw - Bump) : 0f);   // + hard limit past full bump
        Farb = arbForce;
        rcHeight = RC(Mathf.Clamp(travel, -Rebound, Bump));
        // jacking: lateral force (previous step) toward car centre * h_rc / half-track (swing-arm equivalent)
        float fyIn = left ? force.y : -force.y;
        Fjack = p.useJacking ? fyIn * rcHeight / Mathf.Max(halfTrack, 0.3f) : 0f;
        Fanti = antiCoef * force.x;
        Fsusp = Fspring + Fdamper + Fbump + Farb + Fjack + Fanti;
    }
    // ---- low-speed "stick" model (contact-patch bristle, Pacejka "Tire and Vehicle Dynamics" 3rd ed. sec. 7.2 / 8.4 transient slip) ----
    // State: contact-patch deflections dx, dy (m, wheel frame) with  d(delta)/dt = -Vs - |Vx| * delta / sigma  (Vs = contact slip velocity).
    // At speed this converges to delta = sigma * slip, so the spring force k*delta = C * slip with k = C / sigma reproduces the Pacejka
    // slip stiffness exactly (C_kappa = B*C*D = pKx1*Fz, C_alpha = Ky, both at the current load). At standstill |Vx| -> 0, the leak vanishes
    // and the deflection is a spring that holds the car (static friction) instead of the viscous creep of a slip-based model.
    // Damping c = 2*zeta*sqrt(k*m_corner), m_corner = static corner load / g. ASSUMPTION: zeta = 1 (critically damped patch mode; the
    // real tread/carcass hysteresis is not modelled, and a parked car must not ring at the ~3 Hz patch-spring frequency).
    // Limit: friction ellipse with the Pacejka peak coefficients D_x, D_y (load-sensitive, camber factor) -> full static grip mu*Fz;
    // beyond it the patch slides and the deflection is reset to the elastic limit.
    // Blend: weight w of the slip-based Pacejka path = smoothstep over contact speed from vB/2 to vB, vB = the slip-regularisation floor
    // max(minDen, 0.5 m/s) below which the slip-based path no longer sees the true slip. Above vB the original path is used unchanged.
    public const float StickZeta = 1f;
    [HideInInspector] public float stickDx, stickDy, stickWeight;   // deflections (m); weight of the stick model in the force (diagnostics)
    // tire thermal model (option): null when off; ts = Pacejka multipliers (TireScale.One when off -> bit-identical forces)
    public TireThermal thermal; public TireScale ts = TireScale.One; double thFric, thRR, thT;
    public void ThermalStep(double coldGauge, bool warm, double warmCarcass)
    {
        if (thermal == null) { thermal = new TireThermal(coldGauge, warm, warmCarcass); thFric = thRR = thT = 0; }
        thermal.SetColdPressure(coldGauge);
        double T = thT > 0 ? thT : Time.fixedDeltaTime;
        thermal.pFricBulk = thBulk / T;
        thermal.Step(T, thFric / T, thRR / T, Vector3.Dot(rb.GetPointVelocity(transform.position), transform.forward), Fz, camberDeg * Mathf.Deg2Rad, p.tireRate, grounded);
        thFric = thRR = thT = thBulk = 0; ts = thermal.Scale();
    }
    // ---- Experimental (viscoelastic) compound (ViscoTire.cs); never reached with the Default compound ----
    ViscoTire.Flags veFlags; double veVsRef = -1, thBulk;
    [HideInInspector] public float veGrip = 1f, veFlashK, veKScale = 1f, veSigScale = 1f, veRrScale = 1f, veMu;   // diagnostics
    void ViscoUpdate(float vx, float vy, ref float sigma, ref TireScale t, ref float rrScale)
    {
        veFlags = ViscoTire.FromParams(p); if (veVsRef < 0) veVsRef = ViscoTire.VsRef(p);
        double T = thermal != null ? thermal.GripTemp() : TireThermal.TOpt;              // base coefficients = tyre at its optimum (D, as TireThermal)
        double sigP = thermal != null ? System.Math.Max(thermal.pGauge, 20000.0) : TireThermal.PNom;   // nominal contact pressure ~ inflation
        double vsx = vx - omega * p.wheelRadius, vs = System.Math.Sqrt(vsx * vsx + vy * vy);
        double g = ViscoTire.GripFactor(vs, T, sigP, veVsRef, veFlags, out double dT);
        t.mux *= (float)g; t.muy *= (float)g; veGrip = (float)g; veFlashK = (float)dT;
        float FzE = Mathf.Max(Fz, 1f); double k = 1.0;
        if (veFlags.rateStiffness)
        {
            double L = FzE / (sigP * TireThermal.TreadW), Lr = p.Fz0 / (TireThermal.PNom * TireThermal.TreadW);   // contact length (D, membrane approx.)
            k = ViscoTire.StiffnessFactor(vx, L, Lr); t.kx *= (float)k; t.ky *= (float)k;
        }
        veKScale = (float)k;
        if (veFlags.loadRelax)
        {   // sigma = C_Falpha / c_Fy with constant lateral carcass stiffness c_Fy (Pacejka 2012 ch. 7, S; c_Fy const E): sigma scales with C_Falpha
            float ca = t.ky * Mathf.Sin(2f * Mathf.Atan(FzE / (p.pKy2 * t.kyLoad * p.Fz0))), ca0 = Mathf.Sin(2f * Mathf.Atan(1f / p.pKy2));
            sigma *= ca / ca0;
        }
        veSigScale = sigma / p.relaxLength;
        if (veFlags.rollingRes)
        {
            double Trr = thermal != null ? TireThermal.RrToTread * thermal.Tb + (1 - TireThermal.RrToTread) * thermal.Tc : ViscoTire.TRrRef;
            rrScale = (float)ViscoTire.RollingFactor(vx, p.wheelRadius, Trr);
        }
        veRrScale = rrScale;
    }
    /// tread deformation hysteresis (brush model, Pacejka ch. 3): tread elements renew every L/V, each storing F^2 L share / (4 C) in the patch
    /// (C_F = 2 c_p a^2, a = L/2), of which ~pi tan d is lost per load-unload pass (E: half-cycle loss). Power = pi tan d V share sum F^2/(4 C);
    /// contact length cancels. This part of the friction power is dissipated in the tread bulk instead of the surface (TireThermal clamps it).
    float BulkHysteresisPower(float vx, float fx, float fy, in TireScale t)
    {
        if (thermal == null) return 0f;
        float FzE = Mathf.Max(Fz, 1f);
        double cx = p.pKx1 * t.kx * FzE, cy = p.pKy1 * t.ky * p.Fz0 * Mathf.Sin(2f * Mathf.Atan(FzE / (p.pKy2 * t.kyLoad * p.Fz0)));
        double L = FzE / (System.Math.Max(thermal.pGauge, 20000.0) * TireThermal.TreadW), v = System.Math.Abs(vx);
        double td = ViscoTire.TanDelta(System.Math.Max(v, 0.5) / L, thermal.Tb);
        return (float)(System.Math.PI * td * v * ViscoTire.TreadShare * (fx * fx / (4 * cx) + fy * fy / (4 * cy)));
    }
    public void ThermalOff() { thermal = null; ts = TireScale.One; }
    public void TireStep(float h, float forceScale)
    {
        float I = p.wheelInertia + DriveInertia;
        if (!grounded)
        {
            omega += (driveTorque - Mathf.Sign(omega) * Mathf.Min(brakeTorque, Mathf.Abs(omega) * I / h)) / I * h;
            force = Vector2.zero; slipRatio = 0f; stickDx = stickDy = 0f; stickWeight = 0f; return;
        }
        Quaternion steer = Quaternion.AngleAxis(steerDeg, transform.up);
        Vector3 fwd = steer * transform.forward, right = steer * transform.right;
        Vector3 vel = rb.GetPointVelocity(hit.point);
        float vx = Vector3.Dot(vel, fwd), vy = Vector3.Dot(vel, right);
        float minDen = 1.5f * p.pKx1 * Mathf.Max(Fz, 500f) * p.wheelRadius * p.wheelRadius * h / I;
        float vB = Mathf.Max(minDen, 0.5f);
        float den = Mathf.Max(Mathf.Abs(vx), vB);
        float srMF = Mathf.Clamp((omega * p.wheelRadius - vx) / den, -1.5f, 1.5f);
        slipRatio = srMF;
        slipAngle = Mathf.Atan2(vy, Mathf.Max(Mathf.Abs(vx), 0.5f));
        float sigma = p.relaxLength, rrScale = 1f; TireScale tsF = ts;   // Default compound: exactly the original values
        bool ve = p.tireCompound == CarParams.TireCompound.ExperimentalViscoelastic;
        if (ve) ViscoUpdate(vx, vy, ref sigma, ref tsF, ref rrScale);
        float kRel = Mathf.Min(1f, (Mathf.Abs(vx) + 0.5f) * h / sigma);
        alphaRelaxed += kRel * (slipAngle - alphaRelaxed);
        float gamma = camberDeg * Mathf.Deg2Rad * (left ? -1f : 1f);
        force = PacejkaTire.Combined(p, srMF, -alphaRelaxed, Fz, gamma, ve ? tsF : ts);
        float kmhW = vx * 3.6f, rr = (p.rollingRes + p.rollingResV2 * kmhW * kmhW) * Fz * ts.rr * Mathf.Clamp(vx, -1f, 1f);
        if (ve) rr *= rrScale;   // speed-dependent radial-tire RR (Wong form, E)
        force.x -= rr;
        float vC = Mathf.Sqrt(vx * vx + vy * vy);
        float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f * vB, vB, vC)); stickWeight = 1f - w;
        float sig = sigma, ax = Mathf.Abs(vx);
        if (w >= 1f)
        {   // original slip-based path (unchanged); the patch state tracks its steady value so the stick model takes over smoothly
            float wNoBrake0 = omega + (driveTorque - force.x * p.wheelRadius) / I * h;
            float dwBrake0 = brakeTorque / I * h;
            omega = Mathf.Abs(wNoBrake0) <= dwBrake0 ? 0f : wNoBrake0 - Mathf.Sign(wNoBrake0) * dwBrake0;
            stickDx += h * (-(vx - omega * p.wheelRadius) - ax * stickDx / sig); stickDy += h * (-vy - ax * stickDy / sig);
            ClampPatch(Fz, gamma, sig);
        }
        else
        {
            float FzE = Mathf.Max(Fz, 0f);
            float Ckx = p.pKx1 * ts.kx * FzE;                                                             // dFx/dkappa at 0 (= B*C*D)
            float Cky = p.pKy1 * ts.ky * p.Fz0 * Mathf.Sin(2f * Mathf.Atan(FzE / (p.pKy2 * ts.kyLoad * p.Fz0)));       // Ky = dFy/dalpha at 0
            float kx = Ckx / sig, ky = Cky / sig;
            float mC = Mathf.Max(unsprungStaticLoad, 1f) / 9.81f;
            float cx = 2f * StickZeta * Mathf.Sqrt(kx * mC), cy = 2f * StickZeta * Mathf.Sqrt(ky * mC);
            float R = p.wheelRadius, b = 1f - w;
            // wheel spin: Fx = w*Fpac + b*(kx*dx - cx*(vx - omega*R)); damping term implicit (stiff vs the wheel inertia at 2 kHz)
            float a = h * b * cx * R * R / I;
            float wNoBrake = (omega + h / I * (driveTorque - R * (w * force.x + b * (kx * stickDx - cx * vx - rr)))) / (1f + a);
            float dwBrake = brakeTorque / I * h / (1f + a);
            omega = Mathf.Abs(wNoBrake) <= dwBrake ? 0f : wNoBrake - Mathf.Sign(wNoBrake) * dwBrake;
            float vsx = vx - omega * R, vsy = vy;
            stickDx += h * (-vsx - ax * stickDx / sig); stickDy += h * (-vsy - ax * stickDy / sig);
            float fx = kx * stickDx - cx * vsx, fy = ky * stickDy - cy * vsy;
            float dfz = (FzE - p.Fz0) / p.Fz0;
            float Dx = Mathf.Max((p.pDx1 + p.pDx2 * dfz) * ts.mux * FzE, 1f), Dy = Mathf.Max((p.pDy1 + p.pDy2 * dfz) * ts.muy * (1f - 4f * gamma * gamma) * FzE, 1f);
            float e = (fx / Dx) * (fx / Dx) + (fy / Dy) * (fy / Dy);
            if (e > 1f) { float sc = 1f / Mathf.Sqrt(e); fx *= sc; fy *= sc; stickDx = fx / kx; stickDy = fy / ky; }   // sliding: elastic part at the limit
            force = new Vector2(w * force.x + b * (fx - rr), w * force.y + b * fy);
            // reported slip: the true slip ratio (a locked, sliding wheel reads -1); below 5 cm/s the ratio is undefined -> 0/0 guard
            float srTrue = Mathf.Clamp((omega * R - vx) / Mathf.Max(ax, 0.05f), -1.5f, 1.5f);
            slipRatio = w * srMF + b * srTrue;
        }
        if (thermal != null)
        {   // thermal inputs: friction power -F.Vs (tire force without RR) and rolling-resistance power, applied force scale included
            float vsx = vx - omega * p.wheelRadius;
            thFric += -((force.x + rr) * vsx + force.y * vy) * forceScale * h; thRR += rr * vx * forceScale * h; thT += h;
            if (ve && veFlags.bulkHeat) thBulk += BulkHysteresisPower(vx, force.x + rr, force.y, tsF) * forceScale * h;
        }
        // longitudinal at the contact patch; lateral at roll-centre height (geometric lateral load transfer)
        Vector3 latPoint = p.useJacking ? hit.point + transform.up * rcHeight : hit.point;
        rb.AddForceAtPosition(fwd * force.x * forceScale, hit.point);
        rb.AddForceAtPosition(right * force.y * forceScale, latPoint);
    }
    void ClampPatch(float FzE, float gamma, float sig)
    {   // keep the tracked patch deflection inside the friction ellipse (what the patch can hold elastically)
        FzE = Mathf.Max(FzE, 0f); if (FzE <= 0f) { stickDx = stickDy = 0f; return; }
        float dfz = (FzE - p.Fz0) / p.Fz0;
        float kx = p.pKx1 * ts.kx * FzE / sig, ky = p.pKy1 * ts.ky * p.Fz0 * Mathf.Sin(2f * Mathf.Atan(FzE / (p.pKy2 * ts.kyLoad * p.Fz0))) / sig;
        float Dx = Mathf.Max((p.pDx1 + p.pDx2 * dfz) * ts.mux * FzE, 1f), Dy = Mathf.Max((p.pDy1 + p.pDy2 * dfz) * ts.muy * (1f - 4f * gamma * gamma) * FzE, 1f);
        float fx = kx * stickDx, fy = ky * stickDy, e = (fx / Dx) * (fx / Dx) + (fy / Dy) * (fy / Dy);
        if (e > 1f) { float sc = 1f / Mathf.Sqrt(e); stickDx *= sc; stickDy *= sc; }
    }
    void LateUpdate()
    {
        if (!visual || p == null) return;
        spinDeg = (spinDeg + omega * Mathf.Rad2Deg * Time.deltaTime) % 360f;
        visual.position = transform.position - transform.up * (p.hubDropDesign - travel);   // travel follows the unsprung DOF when enabled
        float camVis = (left ? 1f : -1f) * camberDeg;   // visual tilt only
        visual.localRotation = Quaternion.Euler(0f, steerDeg, camVis) * Quaternion.Euler(spinDeg, 0f, 0f) * Quaternion.Euler(0f, 0f, 90f);
    }
}
