using System;
using UnityEngine;

/// <summary>
/// v9 (W; the user: "… with camera slowly falling and islands appearing from the watter as the measures pass"): the present mode's CAMERA, a
/// PURE FUNCTION of the song beat. It follows the song along its columns the way OrbitCamera's continuous follow does in play (a waypoint at the
/// middle of every pass of every column, a belt's later passes one slot east), a little ahead of the playhead so the columns about to rise are in
/// the frame, and it falls slowly and steadily over the whole song: from high above the first columns (<see cref="StartAltitude"/>, looking down
/// <see cref="StartPitch"/>) to just above the islands near the sea at the end (<see cref="EndAltitude"/>, <see cref="EndPitch"/>), the height
/// linear in the song's progress. Living motion is calm: a slow yaw sway on the song clock and a gentle lean (a little yaw and roll that eases in
/// and out over two bars) at each section start, alternating sides. The waypoints are joined by a monotone cubic (no overshoot, no kink: the view
/// never jerks at a column boundary). <see cref="Display"/> smooths the pose for the screen (a short critically damped follow that snaps on a jump:
/// enter, a replay, a seek, the loop) and holds it still while paused. Allocation free per frame.
/// </summary>
public sealed class PresentRig
{
    // ---------------------------------------------------------------- tuning (static: a look pass can adjust them live)
    /// <summary>Camera height above the framed ground at the song's start / end (u) and its pitch (degrees down).</summary>
    public static float StartAltitude = 30f, EndAltitude = 6f, StartPitch = 56f, EndPitch = 20f;
    /// <summary>The base heading (OrbitCamera's home is −24°: the song runs away to the right).</summary>
    public static float Yaw = -20f;
    /// <summary>The slow sway (degrees, period in beats) and the section-start lean (yaw and roll degrees, over LeanBars bars).</summary>
    public static float SwayDeg = 2.2f, SwayPeriodBeats = 32f, LeanDeg = 2.5f, LeanRollDeg = 1.1f, LeanBars = 2f;
    /// <summary>How far ahead of the playhead it looks along the columns (beats).</summary>
    public static float AheadBeats = 2f;
    /// <summary>v9 (S17: "frame the column's west edge + its pass width"): the frame keeps every pass platform that is playing (west to east edge, front
    /// to back lane) in view in full, from PresentSea.LeadBeats before its downbeat (it rises then) until ReleaseBeats after its end, and brings the
    /// next ones in gradually from SpanAheadBeats ahead (a platform not yet risen counts only out to AheadReach u past the playing ones until
    /// RampBeats before its rise). Each platform's membership is a ramp, so the span (and the focus, its centre) is continuous. The camera backs out
    /// when the span would not fit (FitPad u around it, filling at most FitFill of the frame each way): a wide or deep column raises it over the
    /// falling schedule, never below it.</summary>
    public static float SpanAheadBeats = 6f, RampBeats = 2.5f, ReleaseBeats = 2.5f, AheadReach = 18f, FitPad = 2.5f, FitFill = 0.86f;
    /// <summary>The focus sits this far toward the camera from the columns' middle (u; OrbitCamera.FrameCentre uses 0.8).</summary>
    public static float FocusToward = 1.2f;
    /// <summary>The field of view while presenting (degrees).</summary>
    public static float Fov = 40f;
    /// <summary>The display follow (s) and the jump (u) beyond which it snaps.</summary>
    public static float DisplaySmooth = 0.12f, SnapDistance = 4f;
    /// <summary>v9 (R): the launch riser's camera breath — a small lift (u) and a field-of-view widening (degrees) by LaunchFx.BreathAt (a pure
    /// function of the beat, so the fall stays steady: the lift is gentle).</summary>
    public static float BreathLift = 0.35f, BreathFov = 5f;

    public struct Pose
    {
        public Vector3 position, focus;
        public Quaternion rotation;
        public float yaw, pitch, roll, distance, altitude, progress, breath;
        /// <summary>The span the frame holds (world x west / east, z front / back) and the distances it needed across and in depth (diagnostics).</summary>
        public float spanW, spanE, spanF, spanK, fitX, fitZ;
    }

    // waypoints: one per (column, pass) at the pass's middle
    double[] wb = new double[0];
    float[] wx = new float[0], wz = new float[0], wh = new float[0];
    float[] mx = new float[0], mz = new float[0], mh = new float[0];   // the monotone cubic's tangents
    // the pass platform's span: west / east x, front / back z (and their tangents)
    float[] sw = new float[0], se = new float[0], sf = new float[0], sk = new float[0], tw = new float[0], te = new float[0], tf = new float[0], tk = new float[0];
    double[] sb = new double[0];   // the span waypoints' beats: each pass's START (its platform is framed in full from its downbeat on)
    double[] eb = new double[0];   // and its END (the same platform is held in the frame until its pass is over): a second curve through the ends
    float[] uw = new float[0], ue = new float[0], uf = new float[0], uk = new float[0];
    float aspect = 16f / 9f;
    int n;
    double[] sections = new double[0];
    double total = 1.0;
    // display
    Vector3 shownPos, posVel; Quaternion shownRot = Quaternion.identity; bool hasShown;
    /// <summary>The pure pose last displayed (its target; the camera shows it smoothed).</summary>
    public Pose Target { get; private set; }

    public int Waypoints => n;

    /// <summary>Reads the song's columns, passes, belts, grounds and sections.</summary>
    public bool Build(SongManager sm)
    {
        n = 0;
        if (sm == null || !sm.HasSong || sm.ColumnCount == 0) return false;
        total = Math.Max(1.0, sm.TotalBeats);
        int nc = sm.ColumnCount, count = 0;
        for (int c = 0; c < nc; c++) count += Mathf.Max(1, sm.ColumnPasses(c));
        if (wb.Length < count)
        {
            wb = new double[count]; wx = new float[count]; wz = new float[count]; wh = new float[count]; mx = new float[count]; mz = new float[count]; mh = new float[count];
            sw = new float[count]; se = new float[count]; sf = new float[count]; sk = new float[count]; tw = new float[count]; te = new float[count]; tf = new float[count]; tk = new float[count];
            sb = new double[count]; eb = new double[count]; uw = new float[count]; ue = new float[count]; uf = new float[count]; uk = new float[count];
        }
        for (int c = 0; c < nc; c++)
        {
            var b = sm.ColumnBounds(c);
            float x = sm.ColumnCenterX(c), z = b.center.z, h = Mathf.Clamp(b.center.y - 0.6f, ProjectConfig.GroundMin, ProjectConfig.GroundMax);
            int np = Mathf.Max(1, sm.ColumnPasses(c));
            double pl = Math.Max(1e-3, sm.PassLength(c));
            bool belt = sm.BeltExtent(c) > 0f;
            float width = sm.ColumnWidth(c), west = sm.ColumnWestEdge(c);
            for (int p = 0; p < np; p++)
            {
                wb[n] = sm.ColumnStart(c) + p * pl + pl * 0.5;
                float slide = p > 0 && belt ? p * (width + ProjectConfig.BeltGap) : 0f;
                wx[n] = x + slide;
                wz[n] = z; wh[n] = h;
                sw[n] = west + slide; se[n] = west + slide + width; sf[n] = b.min.z; sk[n] = b.max.z;
                sb[n] = sm.ColumnStart(c) + p * pl;
                if (n > 0 && sb[n] <= sb[n - 1] + 1e-6) sb[n] = sb[n - 1] + 1e-3;
                eb[n] = sb[n] + pl;
                if (n > 0 && eb[n] <= eb[n - 1] + 1e-6) eb[n] = eb[n - 1] + 1e-3;
                if (n > 0 && wb[n] <= wb[n - 1] + 1e-6) wb[n] = wb[n - 1] + 1e-3;
                n++;
            }
        }
        Tangents(wx, mx, wb); Tangents(wz, mz, wb); Tangents(wh, mh, wb);
        var st = sm.SectionStarts();
        if (sections.Length != st.Count) sections = new double[st.Count];
        for (int i = 0; i < st.Count; i++) sections[i] = sm.ColumnStart(st[i]);
        hasShown = false;
        return n > 0;
    }

    /// <summary>Fritsch–Carlson tangents: a monotone cubic Hermite through the waypoints (no overshoot between two of them).</summary>
    void Tangents(float[] y, float[] m, double[] wb)
    {
        if (n == 1) { m[0] = 0f; return; }
        for (int i = 0; i < n; i++)
        {
            float dl = i > 0 ? (y[i] - y[i - 1]) / (float)(wb[i] - wb[i - 1]) : 0f;
            float dr = i < n - 1 ? (y[i + 1] - y[i]) / (float)(wb[i + 1] - wb[i]) : 0f;
            if (i == 0) m[i] = dr * 0.5f;
            else if (i == n - 1) m[i] = dl * 0.5f;
            else m[i] = dl * dr <= 0f ? 0f : 2f / (1f / dl + 1f / dr);   // harmonic mean: monotone, smooth
        }
    }

    float Eval(float[] y, float[] m, double b) => Eval(y, m, wb, b);
    float Eval(float[] y, float[] m, double[] wb, double b)
    {
        if (n == 0) return 0f;
        if (b <= wb[0]) return y[0];
        if (b >= wb[n - 1]) return y[n - 1];
        int lo = 0, hi = n - 1;
        while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (wb[mid] <= b) lo = mid; else hi = mid; }
        float h = (float)(wb[hi] - wb[lo]);
        float t = (float)((b - wb[lo]) / h);
        float t2 = t * t, t3 = t2 * t;
        return (2f * t3 - 3f * t2 + 1f) * y[lo] + (t3 - 2f * t2 + t) * h * m[lo] + (-2f * t3 + 3f * t2) * y[hi] + (t3 - t2) * h * m[hi];
    }

    /// <summary>Platform <paramref name="i"/>'s membership of the frame at beat <paramref name="b"/>: 0 → 1 from SpanAheadBeats before its rise (LeadBeats
    /// before its downbeat) to the rise, 1 through its pass, 1 → 0 over ReleaseBeats after its end. <paramref name="reach"/> = the part of the ramp-in
    /// before which (RampBeats ahead of its rise) a platform not yet risen only counts out to AheadReach: 1 from then on.</summary>
    float Member(int i, double b, out float reach)
    {
        double rise = sb[i] - PresentSea.LeadBeats;
        double a0 = rise - SpanAheadBeats;
        float a;
        if (b < a0) a = 0f;
        else if (b < rise) a = (float)((b - a0) / Math.Max(1e-3, rise - a0));
        else if (b < eb[i]) a = 1f;
        else a = 1f - (float)((b - eb[i]) / Math.Max(1e-3, ReleaseBeats));
        reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)((b - (rise - RampBeats)) / Math.Max(1e-3, RampBeats))));
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(a));   // smooth ramps: the span's edges never kink
    }

    /// <summary>The span to keep in view at beat <paramref name="b"/> (world x west / east, z front / back): the members' edges, each pulled toward the
    /// members' weighted centre by how little it is a member yet (continuous), the not-yet-risen ones reach-limited past the playing ones.</summary>
    void Span(double b, out float w, out float e, out float f, out float k)
    {
        float sum = 0f, aw = 0f, ae = 0f, af = 0f, ak = 0f;
        for (int i = 0; i < n; i++) { float r; float a = Member(i, b, out r); if (a <= 0f) continue; sum += a; aw += a * sw[i]; ae += a * se[i]; af += a * sf[i]; ak += a * sk[i]; }
        if (sum <= 1e-6f)
        {
            // before the first platform's window or after the last one's: the nearest one
            int j = b < sb[0] ? 0 : n - 1;
            w = sw[j]; e = se[j]; f = sf[j]; k = sk[j];
            return;
        }
        aw /= sum; ae /= sum; af /= sum; ak /= sum;
        // the playing ones first (their east edge bounds the reach of the ones ahead); a platform counts as playing smoothly over the beat before
        // its downbeat (it rises then), so the reach never jumps when a downbeat passes
        float eHeld = ae;
        for (int i = 0; i < n; i++)
        {
            float r; float a = Member(i, b, out r);
            if (a <= 0f) continue;
            float started = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)((b - (sb[i] - PresentSea.LeadBeats)) / Math.Max(0.05, PresentSea.LeadBeats))));
            eHeld = Mathf.Max(eHeld, Mathf.Lerp(ae, se[i], a * started));
        }
        w = aw; e = ae; f = af; k = ak;
        for (int i = 0; i < n; i++)
        {
            float r; float a = Member(i, b, out r);
            if (a <= 0f) continue;
            float ei = Mathf.Min(se[i], eHeld + AheadReach + r * 1000f);   // a platform not yet risen: only so far past the playing ones
            w = Mathf.Min(w, Mathf.Lerp(aw, sw[i], a)); e = Mathf.Max(e, Mathf.Lerp(ae, ei, a));
            f = Mathf.Min(f, Mathf.Lerp(af, sf[i], a)); k = Mathf.Max(k, Mathf.Lerp(ak, sk[i], a));
        }
    }

    /// <summary>The pose at song beat <paramref name="b"/> (the pre-roll is negative; past the end it holds) for a camera of
    /// <paramref name="aspect"/>.</summary>
    public Pose PoseAt(double b)
    {
        var p = new Pose();
        double look = b + AheadBeats;
        float h = Eval(wh, mh, look);
        float w, e, f, k;
        Span(b, out w, out e, out f, out k);
        float x = (w + e) * 0.5f, z = (f + k) * 0.5f;
        float prog = Mathf.Clamp01((float)(b / total));
        p.progress = prog;
        float breath = 0f;
        try { breath = LaunchFx.BreathAt(b); } catch (Exception) { breath = 0f; }
        p.breath = breath;
        p.altitude = Mathf.Lerp(StartAltitude, EndAltitude, prog) + BreathLift * breath;   // v9: the riser's breath
        p.pitch = Mathf.Lerp(StartPitch, EndPitch, prog);
        float lean = 0f;
        double leanLen = Math.Max(1.0, LeanBars * Math.Max(1, GlobalClock.BeatsPerBar));
        for (int i = 1; i < sections.Length; i++)   // the song's own start has none (the camera is still settling)
        {
            double u = (b - sections[i]) / leanLen;
            if (u <= 0.0 || u >= 1.0) continue;
            float s = Mathf.Sin((float)(Math.PI * u));
            lean += (i % 2 == 0 ? 1f : -1f) * s * s;
        }
        p.yaw = Yaw + SwayDeg * (float)Math.Sin(2.0 * Math.PI * b / Math.Max(1.0, SwayPeriodBeats)) + LeanDeg * lean;
        p.roll = LeanRollDeg * lean;
        var rot = Quaternion.Euler(p.pitch, p.yaw, p.roll);
        p.rotation = rot;
        float yr = p.yaw * Mathf.Deg2Rad;
        Vector3 flatFwd = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
        p.focus = new Vector3(x, h, z) - flatFwd * FocusToward;
        float sinP = Mathf.Max(0.05f, Mathf.Sin(p.pitch * Mathf.Deg2Rad));
        float hv = Mathf.Tan((Fov + BreathFov * breath) * 0.5f * Mathf.Deg2Rad), hh = hv * Mathf.Max(0.5f, aspect);
        float fitX = (e - w + 2f * FitPad) / (2f * hh) / FitFill;                 // the span across the frame
        float fitZ = (k - f + 2f * FitPad) * sinP / (2f * hv) / FitFill;          // the lanes' depth, foreshortened by the pitch
        p.distance = Mathf.Max(p.altitude / sinP, Mathf.Max(fitX, fitZ));
        p.spanW = w; p.spanE = e; p.spanF = f; p.spanK = k; p.fitX = fitX; p.fitZ = fitZ;
        p.altitude = p.distance * sinP;
        p.position = p.focus - rot * Vector3.forward * p.distance;
        return p;
    }

    /// <summary>Puts the pose for beat <paramref name="b"/> on <paramref name="cam"/>: smoothed (DisplaySmooth), snapped when <paramref name="snap"/>
    /// or when the target jumped; nothing moves while <paramref name="hold"/> (paused).</summary>
    public void Display(Camera cam, double b, float dt, bool snap, bool hold)
    {
        if (cam == null) return;
        if (cam.aspect > 0.1f) aspect = cam.aspect;
        var p = PoseAt(b);
        if (!hasShown || snap || (p.position - shownPos).sqrMagnitude > SnapDistance * SnapDistance)
        {
            shownPos = p.position; shownRot = p.rotation; posVel = Vector3.zero; hasShown = true;
        }
        else if (!hold && dt > 0f)
        {
            shownPos = Vector3.SmoothDamp(shownPos, p.position, ref posVel, DisplaySmooth, Mathf.Infinity, dt);
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(1e-3f, DisplaySmooth * 0.6f));
            shownRot = Quaternion.Slerp(shownRot, p.rotation, k);
        }
        cam.transform.SetPositionAndRotation(shownPos, shownRot);
        float fov = Fov + BreathFov * p.breath;   // v9: the riser's breath widens the view
        if (Mathf.Abs(cam.fieldOfView - fov) > 1e-4f) cam.fieldOfView = fov;
        Target = p;
    }

    /// <summary>Forgets the shown pose: the next Display snaps.</summary>
    public void Snap() { hasShown = false; }
}
