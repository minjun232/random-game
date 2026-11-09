// 공용 도우미: 공정 난수, 색상 팔레트, 효과음(WebAudio), 클립보드, 시간 표시.
// 모든 상태는 메모리에만 있고 브라우저 저장소/파일에는 아무것도 쓰지 않는다.
use std::cell::{Cell, RefCell};
use wasm_bindgen::{JsCast, JsValue};

// 룰렛 칸 / 사다리 참가자 / 팀 색상 (10색 순환, v1.2 SlicePalette와 동일)
pub const PALETTE: [&str; 10] = [
    "#E05C5C", "#5B6CF0", "#2EA078", "#D69838", "#9268E0",
    "#D65C96", "#289CB4", "#DE7846", "#6EA046", "#5484D6",
];

pub fn color(i: usize) -> &'static str {
    PALETTE[i % PALETTE.len()]
}

/// "#RRGGBB" + alpha -> "rgba(r,g,b,a)"
pub fn with_alpha(hex: &str, a: f64) -> String {
    let (r, g, b) = hex_rgb(hex);
    format!("rgba({},{},{},{})", r, g, b, a)
}

pub fn lighten(hex: &str, amount: f64) -> String {
    let (r, g, b) = hex_rgb(hex);
    let f = |c: u8| (c as f64 + (255.0 - c as f64) * amount).min(255.0) as u8;
    format!("rgb({},{},{})", f(r), f(g), f(b))
}

fn hex_rgb(hex: &str) -> (u8, u8, u8) {
    let h = hex.trim_start_matches('#');
    let p = |i: usize| u8::from_str_radix(&h[i..i + 2], 16).unwrap_or(0);
    (p(0), p(2), p(4))
}

// ---------------------------------------------------------------------------
// 공정 난수 (crypto.getRandomValues + 거부 샘플링, 원본 NextFairRandomInt와 동일)
// ---------------------------------------------------------------------------
fn rand_u32() -> u32 {
    let mut b = [0u8; 4];
    getrandom::getrandom(&mut b).expect("crypto random unavailable");
    u32::from_le_bytes(b)
}

pub fn rand_int(max_exclusive: usize) -> usize {
    if max_exclusive <= 1 {
        return 0;
    }
    let m = max_exclusive as u32;
    let limit = u32::MAX - (u32::MAX % m);
    loop {
        let v = rand_u32();
        if v < limit {
            return (v % m) as usize;
        }
    }
}

pub fn rand_f64() -> f64 {
    rand_u32() as f64 / (u32::MAX as f64 + 1.0)
}

pub fn shuffle<T>(v: &mut [T]) {
    for i in (1..v.len()).rev() {
        let j = rand_int(i + 1);
        v.swap(i, j);
    }
}

// ---------------------------------------------------------------------------
// 효과음: 원본 WAV 합성식을 그대로 AudioBuffer로 만든다 (외부 파일 없음)
// ---------------------------------------------------------------------------
struct Audio {
    ctx: web_sys::AudioContext,
    tick: web_sys::AudioBuffer,
    reach: web_sys::AudioBuffer,
    win: web_sys::AudioBuffer,
}

thread_local! {
    static AUDIO: RefCell<Option<Audio>> = const { RefCell::new(None) };
    static SOUND_ON: Cell<bool> = const { Cell::new(true) };
    static LAST_TICK_MS: Cell<f64> = const { Cell::new(-1000.0) };
}

const RATE: f32 = 44100.0;

fn tick_samples() -> Vec<f32> {
    let n = (RATE as f64 * 0.014) as usize;
    (0..n)
        .map(|i| {
            let t = i as f64 / RATE as f64;
            let env = (-t * 260.0).exp();
            let freq = 950.0 - 450.0 * (i as f64 / n as f64);
            ((2.0 * std::f64::consts::PI * freq * t).sin() * env * 0.24) as f32
        })
        .collect()
}

fn melody_samples(freqs: &[f64], durs: &[f64], volume: f64) -> Vec<f32> {
    let mut out = Vec::new();
    for (f, d) in freqs.iter().zip(durs) {
        let count = (RATE as f64 * d) as usize;
        for i in 0..count {
            let t = i as f64 / RATE as f64;
            let p = i as f64 / count as f64;
            let attack = if p < 0.08 { p / 0.08 } else { 1.0 };
            let decay = (-p * 3.2).exp();
            let pi2 = 2.0 * std::f64::consts::PI;
            let wave = (pi2 * f * t).sin() * 0.8 + (pi2 * f * 2.0 * t).sin() * 0.2;
            out.push((wave * attack * decay * volume) as f32);
        }
    }
    out
}

fn make_buffer(ctx: &web_sys::AudioContext, mut s: Vec<f32>) -> Option<web_sys::AudioBuffer> {
    let buf = ctx.create_buffer(1, s.len() as u32, RATE).ok()?;
    buf.copy_to_channel(&mut s[..], 0).ok()?;
    Some(buf)
}

fn init_audio() -> Option<Audio> {
    let ctx = web_sys::AudioContext::new().ok()?;
    let tick = make_buffer(&ctx, tick_samples())?;
    let reach = make_buffer(&ctx, melody_samples(&[659.25, 880.0], &[0.07, 0.11], 0.28))?;
    let win = make_buffer(
        &ctx,
        melody_samples(&[523.25, 659.25, 783.99, 1046.50], &[0.075, 0.075, 0.085, 0.22], 0.32),
    )?;
    Some(Audio { ctx, tick, reach, win })
}

#[derive(Clone, Copy)]
pub enum Sfx {
    Tick,
    Reach,
    Win,
}

pub fn set_sound_enabled(on: bool) {
    SOUND_ON.with(|c| c.set(on));
}

pub fn sound_enabled() -> bool {
    SOUND_ON.with(|c| c.get())
}

pub fn play(sfx: Sfx) {
    if !sound_enabled() {
        return;
    }
    if let Sfx::Tick = sfx {
        let now = now_ms();
        let too_soon = LAST_TICK_MS.with(|c| {
            if now - c.get() < 48.0 {
                true
            } else {
                c.set(now);
                false
            }
        });
        if too_soon {
            return;
        }
    }
    AUDIO.with(|cell| {
        let mut slot = cell.borrow_mut();
        if slot.is_none() {
            *slot = init_audio();
        }
        let Some(a) = slot.as_ref() else { return };
        if a.ctx.state() == web_sys::AudioContextState::Suspended {
            let _ = a.ctx.resume();
        }
        let buf = match sfx {
            Sfx::Tick => &a.tick,
            Sfx::Reach => &a.reach,
            Sfx::Win => &a.win,
        };
        if let Ok(src) = a.ctx.create_buffer_source() {
            src.set_buffer(Some(buf));
            let _ = src.connect_with_audio_node(&a.ctx.destination());
            let _ = src.start();
        }
    });
}

// ---------------------------------------------------------------------------
// 브라우저 도우미
// ---------------------------------------------------------------------------
pub fn now_ms() -> f64 {
    web_sys::window()
        .and_then(|w| w.performance())
        .map(|p| p.now())
        .unwrap_or(0.0)
}

pub fn hhmmss() -> String {
    let d = js_sys::Date::new_0();
    format!("{:02}:{:02}:{:02}", d.get_hours(), d.get_minutes(), d.get_seconds())
}

pub fn hhmm() -> String {
    let d = js_sys::Date::new_0();
    format!("{:02}:{:02}", d.get_hours(), d.get_minutes())
}

pub fn alert(msg: &str) {
    if let Some(w) = web_sys::window() {
        let _ = w.alert_with_message(msg);
    }
}

pub fn confirm(msg: &str) -> bool {
    web_sys::window()
        .and_then(|w| w.confirm_with_message(msg).ok())
        .unwrap_or(false)
}

pub fn prompt(msg: &str, default: &str) -> Option<String> {
    web_sys::window().and_then(|w| w.prompt_with_message_and_default(msg, default).ok().flatten())
}

/// navigator.clipboard.writeText (web-sys의 unstable API 플래그 없이 호출)
pub fn copy_text(s: &str) -> bool {
    let Some(w) = web_sys::window() else { return false };
    let nav: JsValue = w.navigator().into();
    let Ok(cb) = js_sys::Reflect::get(&nav, &"clipboard".into()) else { return false };
    if cb.is_undefined() {
        return false;
    }
    let Ok(f) = js_sys::Reflect::get(&cb, &"writeText".into()) else { return false };
    match f.dyn_ref::<js_sys::Function>() {
        Some(f) => f.call1(&cb, &JsValue::from_str(s)).is_ok(),
        None => false,
    }
}

/// 입력 중(입력창/메모장)이면 단축키를 막기 위한 검사
pub fn focused_is_text_input() -> bool {
    let Some(doc) = web_sys::window().and_then(|w| w.document()) else { return false };
    match doc.active_element() {
        Some(el) => {
            let tag = el.tag_name().to_ascii_uppercase();
            tag == "INPUT" || tag == "TEXTAREA" || tag == "SELECT"
        }
        None => false,
    }
}

pub fn blur_active() {
    if let Some(el) = web_sys::window()
        .and_then(|w| w.document())
        .and_then(|d| d.active_element())
        .and_then(|e| e.dyn_into::<web_sys::HtmlElement>().ok())
    {
        let _ = el.blur();
    }
}

/// '꽝', '통과' 같은 일반 문구가 아니면 당첨(강조) 결과로 본다 (사다리 · 제비뽑기 공용)
pub fn is_special_result_text(txt: &str) -> bool {
    let t = txt.trim();
    if t.is_empty() {
        return false;
    }
    !matches!(t, "꽝" | "통과" | "패스" | "-" | "X" | "x" | "잘 마실게요" | "면제")
}

/// 한 줄에 하나(선택적으로 쉼표도) 나눠 공백 제거한 비어 있지 않은 줄 목록
pub fn split_lines(text: &str, comma: bool) -> Vec<String> {
    text.split(|c: char| c == '\r' || c == '\n' || (comma && c == ','))
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
        .collect()
}

/// 캔버스를 부모 크기 × devicePixelRatio로 맞추고 (css폭, css높이, 2d 컨텍스트)를 돌려준다.
pub fn prepare_canvas(
    canvas: &web_sys::HtmlCanvasElement,
) -> Option<(f64, f64, web_sys::CanvasRenderingContext2d)> {
    let dpr = web_sys::window().map(|w| w.device_pixel_ratio()).unwrap_or(1.0);
    let parent = canvas.parent_element()?;
    let w = parent.client_width() as f64;
    let h = parent.client_height() as f64;
    if w < 10.0 || h < 10.0 {
        return None;
    }
    let pw = (w * dpr).round() as u32;
    let ph = (h * dpr).round() as u32;
    if canvas.width() != pw {
        canvas.set_width(pw);
    }
    if canvas.height() != ph {
        canvas.set_height(ph);
    }
    let ctx = canvas
        .get_context("2d")
        .ok()??
        .dyn_into::<web_sys::CanvasRenderingContext2d>()
        .ok()?;
    let _ = ctx.set_transform(dpr, 0.0, 0.0, dpr, 0.0, 0.0);
    ctx.clear_rect(0.0, 0.0, w, h);
    Some((w, h, ctx))
}

pub fn round_rect(ctx: &web_sys::CanvasRenderingContext2d, x: f64, y: f64, w: f64, h: f64, r: f64) {
    let r = r.min(w / 2.0).min(h / 2.0);
    ctx.begin_path();
    ctx.move_to(x + r, y);
    let _ = ctx.arc_to(x + w, y, x + w, y + h, r);
    let _ = ctx.arc_to(x + w, y + h, x, y + h, r);
    let _ = ctx.arc_to(x, y + h, x, y, r);
    let _ = ctx.arc_to(x, y, x + w, y, r);
    ctx.close_path();
}

pub const FONT_STACK: &str = "'Pretendard','Malgun Gothic','Apple SD Gothic Neo','Noto Sans KR',sans-serif";

/// 최대 폭을 넘으면 말줄임(…) 처리한 문자열
pub fn clip_text(ctx: &web_sys::CanvasRenderingContext2d, text: &str, max_w: f64) -> String {
    let width = |s: &str| ctx.measure_text(s).map(|m| m.width()).unwrap_or(0.0);
    if width(text) <= max_w || text.chars().count() <= 2 {
        return text.to_string();
    }
    let mut chars: Vec<char> = text.chars().collect();
    while chars.len() > 1 {
        chars.pop();
        let s: String = chars.iter().collect::<String>() + "…";
        if width(&s) <= max_w {
            return s;
        }
    }
    text.to_string()
}

pub fn truncate_chars(s: &str, max: usize) -> String {
    if s.chars().count() <= max {
        s.to_string()
    } else {
        s.chars().take(max - 1).collect::<String>() + "…"
    }
}
