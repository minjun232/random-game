// 탭 1: 사다리 게임 (가림막 · 가로선 직접 추가/삭제 · 순서대로 자동 출발)
use crate::util::{self, Sfx};
use crate::{tab_accepts_keys, use_modal, ActiveTab};
use leptos::*;

#[derive(Clone)]
struct Bridge {
    left: usize,
    ny: f64, // 0(위) ~ 1(아래)
    custom: bool,
}

struct LdState {
    count: usize,
    dense: bool,
    players: Vec<String>,
    results: Vec<String>,
    bridges: Vec<Bridge>,
    paths: Vec<Vec<(usize, f64)>>,
    end_cols: Vec<usize>,
    completed: Vec<bool>,
    hide: bool,
    revealed: bool,
    anim_player: Option<usize>,
    highlight: Option<usize>,
    auto_seq: bool,
    status: String,
}

impl LdState {
    fn rows(&self) -> usize {
        if self.dense { 18 } else { 12 }
    }

    fn show_curtain(&self) -> bool {
        self.hide && !self.revealed
    }

    fn next_uncompleted(&self) -> Option<usize> {
        (0..self.count).find(|&i| !self.completed.get(i).copied().unwrap_or(false))
    }

    fn generate(&mut self) {
        let n = self.count;
        let rows = self.rows();
        let cols = n.saturating_sub(1).max(1);
        let mut grid = vec![vec![false; cols]; rows];
        for row in grid.iter_mut() {
            for c in 0..n.saturating_sub(1) {
                if c > 0 && row[c - 1] {
                    continue;
                }
                if util::rand_int(100) < 44 {
                    row[c] = true;
                }
            }
        }
        // 다리가 하나도 없는 칸 사이에는 최소 1개 보장
        for c in 0..n.saturating_sub(1) {
            if (0..rows).any(|r| grid[r][c]) {
                continue;
            }
            for _ in 0..24 {
                let rr = util::rand_int(rows);
                let left = c > 0 && grid[rr][c - 1];
                let right = c + 1 < n - 1 && grid[rr][c + 1];
                if !left && !right {
                    grid[rr][c] = true;
                    break;
                }
            }
        }
        self.bridges.clear();
        for (r, row) in grid.iter().enumerate() {
            let ny = (r as f64 + 1.0) / (rows as f64 + 1.0);
            for c in 0..n.saturating_sub(1) {
                if row[c] {
                    self.bridges.push(Bridge { left: c, ny, custom: false });
                }
            }
        }
        self.completed = vec![false; n];
        self.anim_player = None;
        self.highlight = None;
        self.auto_seq = false;
        self.revealed = false; // 새 사다리는 다시 가림
        self.rebuild_paths();
        self.status = "새 사다리 준비 완료! 세로줄 사이 클릭으로 선 추가(우클릭 삭제) 또는 Space로 출발하세요.".into();
    }

    fn rebuild_paths(&mut self) {
        let n = self.count;
        let mut sorted = self.bridges.clone();
        sorted.sort_by(|a, b| a.ny.partial_cmp(&b.ny).unwrap_or(std::cmp::Ordering::Equal).then(a.left.cmp(&b.left)));
        self.paths.clear();
        self.end_cols.clear();
        for start in 0..n {
            let mut pts = vec![(start, 0.0)];
            let mut cur = start;
            for b in &sorted {
                if b.left == cur {
                    pts.push((cur, b.ny));
                    cur += 1;
                    pts.push((cur, b.ny));
                } else if cur > 0 && b.left == cur - 1 {
                    pts.push((cur, b.ny));
                    cur -= 1;
                    pts.push((cur, b.ny));
                }
            }
            pts.push((cur, 1.0));
            self.paths.push(pts);
            self.end_cols.push(cur);
        }
    }

    fn near_collision(&self, left: usize, y: f64) -> bool {
        self.bridges
            .iter()
            .any(|b| (b.left as i64 - left as i64).abs() <= 1 && (b.ny - y).abs() < 0.0055)
    }

    fn clear_completed(&mut self) -> bool {
        let any = self.completed.iter().any(|c| *c);
        self.completed.iter_mut().for_each(|c| *c = false);
        any
    }

    fn resize_lists(&mut self) {
        while self.players.len() < self.count {
            let k = self.players.len() + 1;
            self.players.push(format!("참가자 {}", k));
        }
        self.players.truncate(self.count);
        while self.results.len() < self.count {
            self.results.push("꽝".into());
        }
        self.results.truncate(self.count);
    }

    fn result_of(&self, p: usize) -> String {
        self.end_cols.get(p).and_then(|c| self.results.get(*c)).cloned().unwrap_or_default()
    }
}

#[derive(Default)]
struct Hover {
    player: Option<usize>,
    add: Option<(usize, f64)>,
    del: Option<usize>,
}

struct Anim {
    t0: f64,
    progress: f64,
    last_tick: i32,
    running: bool,
}

#[derive(Clone, Copy)]
struct Lad {
    st: RwSignal<LdState>,
    hover: StoredValue<Hover>,
    anim: StoredValue<Anim>,
    canvas: NodeRef<html::Canvas>,
    rows_rev: RwSignal<u32>,
    copy_fb: RwSignal<String>,
}

const TOP_Y: f64 = 48.0;

fn col_x(col: usize, count: usize, w: f64) -> f64 {
    if count <= 1 {
        return w * 0.5;
    }
    let pad = (w / (count as f64 * 1.6)).clamp(44.0, 70.0);
    let usable = (w - pad * 2.0).max(40.0);
    pad + usable * (col as f64 / (count - 1) as f64)
}

fn pill_w(count: usize, w: f64) -> f64 {
    let spacing = if count > 1 { (w - 88.0) / (count - 1) as f64 } else { 120.0 };
    (spacing * 0.86).clamp(42.0, 92.0)
}

impl Lad {
    fn draw(self) {
        let Some(c) = self.canvas.get_untracked() else { return };
        let progress = self.anim.with_value(|a| a.progress);
        self.st.with_untracked(|s| self.hover.with_value(|h| draw_ladder(&c, s, h, progress)));
    }

    fn canvas_size(self) -> (f64, f64) {
        self.canvas
            .get_untracked()
            .map(|c| (c.client_width() as f64, c.client_height() as f64))
            .unwrap_or((0.0, 0.0))
    }

    fn update_hover(self, x: f64, y: f64) {
        let (w, h) = self.canvas_size();
        let new = self.st.with_untracked(|s| {
            let n = s.count;
            let mut hv = Hover::default();
            if n == 0 || w <= 10.0 || h <= 80.0 {
                return hv;
            }
            let pw = pill_w(n, w);
            for i in 0..n {
                let cx = col_x(i, n, w);
                if x >= cx - pw / 2.0 - 4.0 && x <= cx + pw / 2.0 + 4.0 && y >= 6.0 && y <= 46.0 {
                    hv.player = Some(i);
                    return hv;
                }
            }
            if s.anim_player.is_some() || n < 2 {
                return hv;
            }
            let bot = h - 48.0;
            let rail = (bot - TOP_Y).max(40.0);
            if y < TOP_Y + 12.0 || y > bot - 12.0 {
                return hv;
            }
            let Some(span) = (0..n - 1).find(|&c| x >= col_x(c, n, w) + 3.0 && x <= col_x(c + 1, n, w) - 3.0) else {
                return hv;
            };
            let curtain = s.show_curtain();
            let mut best = 8.0;
            for (i, b) in s.bridges.iter().enumerate() {
                if b.left != span || (curtain && !b.custom) {
                    continue;
                }
                let dy = (y - (TOP_Y + rail * b.ny)).abs();
                if dy <= best {
                    best = dy;
                    hv.del = Some(i);
                }
            }
            if hv.del.is_none() {
                hv.add = Some((span, ((y - TOP_Y) / rail).clamp(0.045, 0.955)));
            }
            hv
        });
        if let Some(c) = self.canvas.get_untracked() {
            let hand = new.player.is_some() || new.add.is_some() || new.del.is_some();
            let el: &web_sys::HtmlElement = &c;
            let _ = el.style().set_property("cursor", if hand { "pointer" } else { "default" });
        }
        self.hover.set_value(new);
        self.draw();
    }

    fn after_bridges_changed(self, add_msg: Option<usize>) {
        let mut any = false;
        self.st.update(|s| {
            any = s.clear_completed();
            s.highlight = None;
            s.rebuild_paths();
            let custom = s.bridges.iter().filter(|b| b.custom).count();
            s.status = match add_msg {
                Some(left) if !any => format!(
                    "✏️ {}번~{}번 줄 사이에 가로선을 추가했습니다 (직접 추가 {}개 · 우클릭 시 삭제)",
                    left + 1, left + 2, custom
                ),
                Some(_) => format!("✏️ 가로선 추가됨 (직접 추가 {}개) — 사다리가 변경되어 경로를 다시 출발합니다.", custom),
                None if any => "🗑️ 가로선을 삭제했습니다 — 변경된 사다리로 다시 출발해 주세요.".into(),
                None => "🗑️ 선택한 가로선을 삭제했습니다.".into(),
            };
        });
    }

    fn add_bridge(self, left: usize, raw: f64) {
        let ok = self.st.with_untracked(|s| s.anim_player.is_none() && left + 1 < s.count);
        if !ok {
            return;
        }
        self.st.update(|s| s.auto_seq = false);
        let base = raw.clamp(0.045, 0.955);
        let chosen = self.st.with_untracked(|s| {
            for step in 0..=20 {
                let d = step as f64 * 0.006;
                let up = base - d;
                if up >= 0.04 && !s.near_collision(left, up) {
                    return Some(up);
                }
                let dn = base + d;
                if dn <= 0.96 && !s.near_collision(left, dn) {
                    return Some(dn);
                }
            }
            None
        });
        let Some(y) = chosen else { return };
        self.st.update(|s| s.bridges.push(Bridge { left, ny: y, custom: true }));
        util::play(Sfx::Tick);
        self.after_bridges_changed(Some(left));
    }

    fn delete_bridge(self, idx: usize) {
        let ok = self.st.with_untracked(|s| {
            s.anim_player.is_none() && idx < s.bridges.len() && !(s.show_curtain() && !s.bridges[idx].custom)
        });
        if !ok {
            return;
        }
        self.st.update(|s| {
            s.auto_seq = false;
            s.bridges.remove(idx);
        });
        util::play(Sfx::Tick);
        self.after_bridges_changed(None);
    }

    fn regenerate(self) {
        self.anim.update_value(|a| a.running = false);
        self.st.update(|s| s.generate());
        self.copy_fb.set(String::new());
    }

    fn reset_traces(self) {
        self.anim.update_value(|a| a.running = false);
        self.st.update(|s| {
            s.auto_seq = false;
            s.anim_player = None;
            s.clear_completed();
            s.highlight = None;
            s.revealed = false;
            s.status = "경로를 초기화했습니다. 다시 출발할 수 있습니다.".into();
        });
    }

    fn start_trace(self, p: usize) {
        self.st.update(|s| {
            s.revealed = true; // 첫 출발 시 가림막을 걷고 이번 판 동안 유지
            s.anim_player = Some(p);
            s.highlight = Some(p);
            s.status = format!("🏃 [{}] 사다리 이동 중... (한 번 더 누르면 즉시 도착)", s.players[p]);
        });
        let start_loop = self.anim.try_update_value(|a| {
            a.t0 = util::now_ms();
            a.progress = 0.0;
            a.last_tick = -1;
            let s = !a.running;
            a.running = true;
            s
        });
        if start_loop == Some(true) {
            request_animation_frame(move || self.frame());
        }
    }

    fn frame(self) {
        if !self.anim.with_value(|a| a.running) || self.st.with_untracked(|s| s.anim_player.is_none()) {
            self.anim.update_value(|a| a.running = false);
            return;
        }
        let dur = if self.st.with_untracked(|s| s.auto_seq) { 0.55 } else { 0.85 };
        let mut tick = false;
        let p = self
            .anim
            .try_update_value(|a| {
                let p = (util::now_ms() - a.t0) / 1000.0 / dur;
                a.progress = p.min(1.0);
                let step = (p * 10.0) as i32;
                if step != a.last_tick {
                    a.last_tick = step;
                    tick = true;
                }
                p
            })
            .unwrap_or(1.0);
        if p >= 1.0 {
            self.finish_anim();
        } else {
            if tick {
                util::play(Sfx::Tick);
            }
            self.draw();
        }
        if self.anim.with_value(|a| a.running) {
            request_animation_frame(move || self.frame());
        }
    }

    fn finish_anim(self) {
        let Some(done) = self.st.with_untracked(|s| s.anim_player) else { return };
        self.anim.update_value(|a| {
            a.progress = 1.0;
            a.running = false;
        });
        let mut res = String::new();
        let mut next = None;
        self.st.update(|s| {
            s.anim_player = None;
            if let Some(c) = s.completed.get_mut(done) {
                *c = true;
            }
            s.highlight = Some(done);
            res = s.result_of(done);
            s.status = format!("✔ [{}] 도착 결과 → [{}]", s.players[done], res);
            if s.auto_seq {
                next = s.next_uncompleted();
                if next.is_none() {
                    s.auto_seq = false;
                }
            }
        });
        util::play(if util::is_special_result_text(&res) { Sfx::Win } else { Sfx::Reach });
        if let Some(n) = next {
            self.start_trace(n);
        }
    }

    fn player_clicked(self, p: usize) {
        util::blur_active();
        if self.st.with_untracked(|s| s.anim_player.is_some()) {
            self.finish_anim();
        }
        let done = self.st.with_untracked(|s| s.completed.get(p).copied().unwrap_or(false));
        if done {
            self.st.update(|s| {
                s.highlight = Some(p);
                s.status = format!("🔎 [{}] → [{}]", s.players[p], s.result_of(p));
            });
            return;
        }
        self.start_trace(p);
    }

    fn space(self) {
        util::blur_active();
        if self.st.with_untracked(|s| s.anim_player.is_some()) {
            self.finish_anim();
            return;
        }
        let next = match self.st.with_untracked(|s| s.next_uncompleted()) {
            Some(n) => n,
            None => {
                self.reset_traces();
                0
            }
        };
        self.start_trace(next);
    }

    fn toggle_auto(self) {
        util::blur_active();
        if self.st.with_untracked(|s| s.auto_seq) {
            self.st.update(|s| s.auto_seq = false);
            return;
        }
        if self.st.with_untracked(|s| s.next_uncompleted().is_none()) {
            self.reset_traces();
        }
        self.st.update(|s| s.auto_seq = true);
        if self.st.with_untracked(|s| s.anim_player.is_none()) {
            if let Some(n) = self.st.with_untracked(|s| s.next_uncompleted()) {
                self.start_trace(n);
            }
        }
    }

    fn reveal_all(self) {
        util::blur_active();
        self.anim.update_value(|a| a.running = false);
        self.st.update(|s| {
            s.auto_seq = false;
            s.anim_player = None;
            s.completed.iter_mut().for_each(|c| *c = true);
            s.revealed = true;
            s.highlight = None;
            s.status = "✨ 모든 참가자의 사다리 결과가 공개되었습니다! (이름을 클릭하면 개별 경로가 강조됩니다)".into();
        });
        util::play(Sfx::Win);
    }

    fn change_count(self, d: i32) {
        let next = self.st.with_untracked(|s| s.count as i32 + d);
        if !(2..=14).contains(&next) {
            return;
        }
        self.st.update(|s| {
            s.count = next as usize;
            s.resize_lists();
        });
        self.rows_rev.update(|v| *v += 1);
        self.regenerate();
        util::blur_active();
    }

    fn apply_results(self, results: Vec<String>) {
        self.st.update(|s| {
            s.results = results;
            util::shuffle(&mut s.results);
        });
        self.rows_rev.update(|v| *v += 1);
        self.regenerate();
        util::blur_active();
    }

    fn copy(self) {
        let text = self.st.with_untracked(|s| {
            let mut out = vec!["[사다리 게임 결과]".to_string()];
            for i in 0..s.count {
                let status = if s.completed.get(i).copied().unwrap_or(false) { s.result_of(i) } else { "(미출발)".into() };
                out.push(format!("{}. {} → {}", i + 1, s.players[i], status));
            }
            out.join("\n")
        });
        if util::copy_text(&text) {
            self.copy_fb.set("✔ 클립보드에 복사되었습니다!".into());
        }
        util::blur_active();
    }
}

fn draw_ladder(canvas: &web_sys::HtmlCanvasElement, s: &LdState, hv: &Hover, progress: f64) {
    let Some((w, h, ctx)) = util::prepare_canvas(canvas) else { return };
    if h <= 80.0 {
        return;
    }
    use std::f64::consts::PI;
    let font = |size: f64, weight: u32| format!("{} {}px {}", weight, size, util::FONT_STACK);

    util::round_rect(&ctx, 0.5, 0.5, w - 1.0, h - 1.0, 10.0);
    ctx.set_fill_style_str("#0D0E11");
    ctx.fill();
    ctx.set_line_width(1.0);
    ctx.set_stroke_style_str("#1A1B20");
    ctx.stroke();

    let n = s.count;
    if n < 2 {
        return;
    }
    let bot = h - 48.0;
    let rail = (bot - TOP_Y).max(40.0);
    let line = |x1: f64, y1: f64, x2: f64, y2: f64| {
        ctx.begin_path();
        ctx.move_to(x1, y1);
        ctx.line_to(x2, y2);
        ctx.stroke();
    };
    let dot = |x: f64, y: f64, r: f64, color: &str| {
        ctx.begin_path();
        let _ = ctx.arc(x, y, r, 0.0, PI * 2.0);
        ctx.set_fill_style_str(color);
        ctx.fill();
    };

    // 1. 세로줄
    ctx.set_line_cap("round");
    ctx.set_line_width(3.0);
    ctx.set_stroke_style_str("#2A2C33");
    for c in 0..n {
        let x = col_x(c, n, w);
        line(x, TOP_Y, x, bot);
    }

    // 2. 가림막 (첫 출발 전에만)
    let curtain = s.show_curtain();
    if curtain {
        let (cx, cy, cw, ch) = (18.0, TOP_Y + 12.0, (w - 36.0).max(20.0), (rail - 24.0).max(24.0));
        util::round_rect(&ctx, cx, cy, cw, ch, 10.0);
        ctx.set_fill_style_str("#171A26");
        ctx.fill();
        ctx.set_line_width(1.4);
        ctx.set_stroke_style_str("#5B6CF0");
        ctx.stroke();
        let dash = js_sys::Array::of2(&3.0.into(), &2.5.into());
        let _ = ctx.set_line_dash(&dash);
        ctx.set_line_width(2.4);
        ctx.set_stroke_style_str("#2E3566");
        for c in 0..n {
            let x = col_x(c, n, w);
            line(x, cy + 2.0, x, cy + ch - 2.0);
        }
        let _ = ctx.set_line_dash(&js_sys::Array::new());
        ctx.set_font(&font(11.5, 600));
        let msg = util::clip_text(&ctx, "❓ 가림막 ON — 출발 전 세로줄 사이 클릭으로 선 추가 가능 (첫 출발 시 공개)", (cw - 28.0).max(40.0));
        let tw = ctx.measure_text(&msg).map(|m| m.width()).unwrap_or(0.0);
        let bx = cx + (cw - (tw + 20.0)) / 2.0;
        util::round_rect(&ctx, bx, cy + 8.0, tw + 20.0, 23.0, 11.0);
        ctx.set_fill_style_str("rgba(28,31,58,0.9)");
        ctx.fill();
        ctx.set_line_width(1.0);
        ctx.set_stroke_style_str("#6878F2");
        ctx.stroke();
        ctx.set_text_align("center");
        ctx.set_text_baseline("middle");
        ctx.set_fill_style_str("#C7CDFB");
        let _ = ctx.fill_text(&msg, cx + cw / 2.0, cy + 19.5);
    }

    // 3. 가로선 (가림막 중에는 직접 추가한 선만)
    for (i, b) in s.bridges.iter().enumerate() {
        if b.left + 1 >= n || (curtain && !b.custom) {
            continue;
        }
        let x1 = col_x(b.left, n, w);
        let x2 = col_x(b.left + 1, n, w);
        let y = TOP_Y + rail * b.ny;
        let del_hover = hv.del == Some(i) && s.anim_player.is_none();
        if del_hover {
            ctx.set_line_width(4.0);
            ctx.set_stroke_style_str("#E5736F");
            line(x1, y, x2, y);
            dot(x1, y, 3.6, "#E5736F");
            dot(x2, y, 3.6, "#E5736F");
        } else if b.custom {
            ctx.set_line_width(3.2);
            ctx.set_stroke_style_str("#8B97F7");
            line(x1, y, x2, y);
            dot(x1, y, 3.0, "#8B97F7");
            dot(x2, y, 3.0, "#8B97F7");
        } else {
            ctx.set_line_width(3.0);
            ctx.set_stroke_style_str("#3A3D46");
            line(x1, y, x2, y);
        }
    }

    // 4~6. 경로 (완료: 흐리게, 강조/진행 중: 진하게)
    let draw_path = |p: usize, prog: f64, color: &str, width: f64, head: bool| {
        let Some(pts) = s.paths.get(p) else { return };
        if pts.len() < 2 {
            return;
        }
        let sp: Vec<(f64, f64)> = pts.iter().map(|(c, ny)| (col_x(*c, n, w), TOP_Y + rail * ny)).collect();
        let total: f64 = sp.windows(2).map(|q| ((q[1].0 - q[0].0).powi(2) + (q[1].1 - q[0].1).powi(2)).sqrt()).sum();
        let target = total * prog.clamp(0.0, 1.0);
        ctx.set_line_width(width);
        ctx.set_stroke_style_str(color);
        ctx.set_line_join("round");
        ctx.begin_path();
        ctx.move_to(sp[0].0, sp[0].1);
        let mut acc = 0.0;
        let mut headp = sp[0];
        for q in sp.windows(2) {
            let seg = ((q[1].0 - q[0].0).powi(2) + (q[1].1 - q[0].1).powi(2)).sqrt();
            if seg <= 0.001 {
                continue;
            }
            if acc + seg <= target {
                ctx.line_to(q[1].0, q[1].1);
                headp = q[1];
                acc += seg;
            } else {
                let r = (target - acc) / seg;
                headp = (q[0].0 + (q[1].0 - q[0].0) * r, q[0].1 + (q[1].1 - q[0].1) * r);
                ctx.line_to(headp.0, headp.1);
                break;
            }
        }
        ctx.stroke();
        if head {
            ctx.begin_path();
            let _ = ctx.arc(headp.0, headp.1, 6.0, 0.0, PI * 2.0);
            ctx.set_fill_style_str("#FFFFFF");
            ctx.fill();
            ctx.stroke();
        }
    };
    for p in 0..n {
        if s.completed.get(p).copied().unwrap_or(false) && s.highlight != Some(p) {
            draw_path(p, 1.0, &util::with_alpha(util::color(p), 0.45), 3.0, false);
        }
    }
    if let Some(hp) = s.highlight {
        if s.anim_player.is_none() && s.completed.get(hp).copied().unwrap_or(false) {
            draw_path(hp, 1.0, util::color(hp), 5.0, true);
        }
    }
    if let Some(ap) = s.anim_player {
        draw_path(ap, progress, util::color(ap), 5.2, true);
    }

    // 7. 가로선 추가 미리보기 / 삭제 안내
    if s.anim_player.is_none() {
        if let Some((col, ny)) = hv.add {
            if col + 1 < n {
                let x1 = col_x(col, n, w);
                let x2 = col_x(col + 1, n, w);
                let y = TOP_Y + rail * ny;
                let dash = js_sys::Array::of2(&6.0.into(), &4.0.into());
                let _ = ctx.set_line_dash(&dash);
                ctx.set_line_width(2.6);
                ctx.set_stroke_style_str("rgba(139,151,247,0.8)");
                line(x1, y, x2, y);
                let _ = ctx.set_line_dash(&js_sys::Array::new());
                let mx = (x1 + x2) / 2.0;
                dot(mx, y, 8.0, "#4A5BE0");
                ctx.set_line_width(1.2);
                ctx.set_stroke_style_str("#C7CDFB");
                ctx.stroke();
                ctx.set_font(&font(12.0, 700));
                ctx.set_text_align("center");
                ctx.set_text_baseline("middle");
                ctx.set_fill_style_str("#FFFFFF");
                let _ = ctx.fill_text("+", mx, y);
            }
        } else if let Some(di) = hv.del {
            if let Some(b) = s.bridges.get(di) {
                let x1 = col_x(b.left, n, w);
                let x2 = col_x(b.left + 1, n, w);
                let y = TOP_Y + rail * b.ny;
                ctx.set_font(&font(10.5, 700));
                let tw = ctx.measure_text("우클릭: 삭제").map(|m| m.width()).unwrap_or(50.0) + 12.0;
                let th = 18.0;
                let tx = ((x1 + x2 - tw) / 2.0).clamp(6.0, w - tw - 6.0);
                let ty = (y - th - 6.0).max(TOP_Y + 4.0);
                util::round_rect(&ctx, tx, ty, tw, th, 5.0);
                ctx.set_fill_style_str("rgba(110,32,32,0.92)");
                ctx.fill();
                ctx.set_line_width(1.0);
                ctx.set_stroke_style_str("#E5736F");
                ctx.stroke();
                ctx.set_text_align("center");
                ctx.set_text_baseline("middle");
                ctx.set_fill_style_str("#F6D3D1");
                let _ = ctx.fill_text("우클릭: 삭제", tx + tw / 2.0, ty + th / 2.0);
            }
        }
    }

    // 8. 위 참가자 / 아래 결과 이름표
    let mut reached: Vec<Option<usize>> = vec![None; n];
    for p in 0..n {
        if s.completed.get(p).copied().unwrap_or(false) {
            if let Some(&d) = s.end_cols.get(p) {
                if d < n {
                    reached[d] = Some(p);
                }
            }
        }
    }
    let fsize = if n >= 11 { 10.5 } else if n >= 8 { 11.5 } else { 12.5 };
    let pw = pill_w(n, w);
    ctx.set_text_align("center");
    ctx.set_text_baseline("middle");
    for i in 0..n {
        let cx = col_x(i, n, w);
        let col = util::color(i);
        let done = s.completed.get(i).copied().unwrap_or(false);
        let hot = hv.player == Some(i) || s.anim_player == Some(i) || s.highlight == Some(i);
        util::round_rect(&ctx, cx - pw / 2.0, 10.0, pw, 32.0, 7.0);
        if hot {
            ctx.set_fill_style_str(col);
        } else if done {
            ctx.set_fill_style_str(&util::with_alpha(col, 0.25));
        } else {
            ctx.set_fill_style_str("#1A1B20");
        }
        ctx.fill();
        ctx.set_line_width(if hot { 2.2 } else { 1.4 });
        ctx.set_stroke_style_str(col);
        ctx.stroke();
        ctx.set_font(&font(fsize, 700));
        ctx.set_fill_style_str("#FFFFFF");
        let label = if s.players[i].is_empty() { format!("{}번", i + 1) } else { s.players[i].clone() };
        let label = util::clip_text(&ctx, &label, pw - 8.0);
        let _ = ctx.fill_text(&label, cx, 26.0);
    }
    for c in 0..n {
        let cx = col_x(c, n, w);
        let res = s.results.get(c).cloned().unwrap_or_default();
        let special = util::is_special_result_text(&res);
        util::round_rect(&ctx, cx - pw / 2.0, h - 42.0, pw, 32.0, 7.0);
        let (bg, stroke, lw, fg) = match reached[c] {
            Some(p) => (
                if special { "#0F3B2B" } else { "#1A1B20" },
                util::color(p).to_string(),
                2.2,
                if special { "#95DDBB" } else { "#FFFFFF" },
            ),
            None => (
                if special { "#1C1F3A" } else { "#111216" },
                (if special { "#E0A243" } else { "#2A2C33" }).to_string(),
                1.3,
                if special { "#F4D99A" } else { "#A0A3AC" },
            ),
        };
        ctx.set_fill_style_str(bg);
        ctx.fill();
        ctx.set_line_width(lw);
        ctx.set_stroke_style_str(&stroke);
        ctx.stroke();
        ctx.set_font(&font(fsize, if special { 700 } else { 600 }));
        ctx.set_fill_style_str(fg);
        let text = util::clip_text(&ctx, if res.is_empty() { "-" } else { &res }, pw - 8.0);
        let _ = ctx.fill_text(&text, cx, h - 26.0);
    }
}

#[component]
pub fn LadderTab() -> impl IntoView {
    let mut init = LdState {
        count: 4,
        dense: false,
        players: (1..=4).map(|i| format!("참가자 {}", i)).collect(),
        results: (0..4).map(|i| if i == 0 { "🎉 당첨!".to_string() } else { "꽝".to_string() }).collect(),
        bridges: vec![],
        paths: vec![],
        end_cols: vec![],
        completed: vec![],
        hide: true,
        revealed: false,
        anim_player: None,
        highlight: None,
        auto_seq: false,
        status: String::new(),
    };
    util::shuffle(&mut init.results);
    init.generate();
    let l = Lad {
        st: create_rw_signal(init),
        hover: store_value(Hover::default()),
        anim: store_value(Anim { t0: 0.0, progress: 0.0, last_tick: -1, running: false }),
        canvas: create_node_ref::<html::Canvas>(),
        rows_rev: create_rw_signal(0),
        copy_fb: create_rw_signal(String::new()),
    };
    let st = l.st;
    let modal = use_modal();
    let canvas_ref = l.canvas;

    // 상태가 바뀔 때마다 다시 그리기 (+ 창 크기 / 탭 전환)
    create_effect(move |_| {
        st.track();
        request_animation_frame(move || l.draw());
    });
    window_event_listener(ev::resize, move |_| l.draw());
    let active = use_context::<ActiveTab>().expect("ActiveTab").0;
    create_effect(move |_| {
        if active.get() == 1 {
            request_animation_frame(move || l.draw());
        } else if st.with_untracked(|s| s.auto_seq) {
            st.update(|s| s.auto_seq = false);
        }
    });

    window_event_listener(ev::keydown, move |e: web_sys::KeyboardEvent| {
        if !tab_accepts_keys(1) || e.is_composing() || util::focused_is_text_input() {
            return;
        }
        match e.code().as_str() {
            "Space" => {
                e.prevent_default();
                if !e.repeat() {
                    l.space();
                }
            }
            "Escape" => st.update(|s| s.auto_seq = false),
            _ => {}
        }
    });

    let on_move = move |e: web_sys::MouseEvent| l.update_hover(e.offset_x() as f64, e.offset_y() as f64);
    let on_down = move |e: web_sys::MouseEvent| {
        l.update_hover(e.offset_x() as f64, e.offset_y() as f64);
        let (player, add, del) = l.hover.with_value(|h| (h.player, h.add, h.del));
        if e.button() == 0 {
            if let Some(p) = player {
                l.player_clicked(p);
            } else if let (Some((col, ny)), None) = (add, del) {
                util::blur_active();
                l.add_bridge(col, ny);
            }
        } else if e.button() == 2 {
            if let Some(d) = del {
                l.delete_bridge(d);
            }
        }
        l.update_hover(e.offset_x() as f64, e.offset_y() as f64);
    };
    let on_leave = move |_| {
        l.hover.set_value(Hover::default());
        l.draw();
    };

    let open_bulk_players = move |_| {
        let text = st.with_untracked(|s| s.players.join("\n"));
        modal.open(
            "상단 출발 참가자 일괄 입력",
            "한 줄에 한 명씩(또는 쉼표 구분) 참가자 이름을 입력하세요 (2~14명).\n인원 수가 바뀌면 하단 도착 칸 개수도 자동으로 맞춰집니다:",
            text,
            move |res| {
                let names: Vec<String> = util::split_lines(&res, true).into_iter().take(14).collect();
                if names.len() < 2 {
                    util::alert("사다리 게임은 최소 2명 이상이어야 합니다.");
                    return;
                }
                st.update(|s| {
                    s.count = names.len();
                    s.players = names;
                    s.resize_lists();
                });
                l.rows_rev.update(|v| *v += 1);
                l.regenerate();
            },
        );
    };

    let open_bulk_results = move |_| {
        let (text, n) = st.with_untracked(|s| (s.results.join("\n"), s.count));
        modal.open(
            "하단 도착 항목 일괄 편집",
            &format!("하단 도착 칸에 들어갈 항목을 한 줄에 하나씩 입력하세요 (현재 {}칸):", n),
            text,
            move |res| {
                let mut r: Vec<String> = util::split_lines(&res, false).into_iter().take(n).collect();
                while r.len() < n {
                    r.push("꽝".into());
                }
                st.update(|s| s.results = r);
                l.rows_rev.update(|v| *v += 1);
            },
        );
    };

    // 입력칸은 인원 수/일괄 편집 때만 다시 만든다 (타이핑 중 포커스 유지)
    let player_inputs = move || {
        l.rows_rev.track();
        st.with_untracked(|s| {
            s.players
                .iter()
                .enumerate()
                .map(|(i, p)| {
                    view! {
                        <div class="entry">
                            <span class="num-badge" style:background=util::color(i)>{i + 1}</span>
                            <input class="input sm" prop:value=p.clone()
                                on:input=move |e| { let v = event_target_value(&e); st.update(|s| s.players[i] = v); }/>
                        </div>
                    }
                })
                .collect_view()
        })
    };
    let result_inputs = move || {
        l.rows_rev.track();
        st.with_untracked(|s| {
            s.results
                .iter()
                .enumerate()
                .map(|(i, r)| {
                    view! {
                        <div class="entry">
                            <span class="slot-tag">{format!("{}칸", (b'A' + i as u8) as char)}</span>
                            <input class="input sm" prop:value=r.clone()
                                on:input=move |e| { let v = event_target_value(&e); st.update(|s| s.results[i] = v); }/>
                        </div>
                    }
                })
                .collect_view()
        })
    };

    let match_list = move || {
        st.with(|s| {
            let rows: Vec<_> = (0..s.count).filter(|&i| s.completed.get(i).copied().unwrap_or(false)).collect();
            if rows.is_empty() {
                return view! { <div class="empty">"아직 도착한 참가자가 없습니다."<br/>"상단 이름을 클릭하거나 출발 버튼을 눌러보세요."</div> }.into_view();
            }
            rows.into_iter()
                .map(|i| {
                    let res = s.result_of(i);
                    let special = util::is_special_result_text(&res);
                    view! {
                        <div class="log-row clickable" class:win=special on:click=move |_| st.update(|s| s.highlight = Some(i))>
                            <span class="row gap6 grow ellipsis"><span class="dot" style:background=util::color(i)></span><b>{s.players[i].clone()}</b></span>
                            <span class="pill solid" class:win=special>{res}</span>
                        </div>
                    }
                })
                .collect_view()
        })
    };

    let animating = move || st.with(|s| s.anim_player.is_some());
    let next_label = move || {
        st.with(|s| {
            if s.anim_player.is_some() {
                "⏩ 즉시 도착 (Space)".to_string()
            } else {
                match s.next_uncompleted() {
                    Some(n) => format!("▶ {} 출발 (Space)", s.players[n]),
                    None => "🔄 다시 출발 (Space)".into(),
                }
            }
        })
    };

    view! {
        <div class="cols ladder">
            <section class="card col">
                <div class="card-head"><h2>"사다리 구성 설정"</h2></div>
                <div class="stepper">
                    <span>"사다리 줄 수 / 인원 (2~14명)"</span>
                    <div class="row">
                        <button class="btn sq" on:click=move |_| l.change_count(-1)>"−"</button>
                        <b class="step-val accent-text">{move || format!("{}명", st.with(|s| s.count))}</b>
                        <button class="btn sq primary" on:click=move |_| l.change_count(1)>"+"</button>
                    </div>
                </div>
                <div class="scroll grow">
                    <div class="subcard accent">
                        <div class="row between mb8">
                            <b class="accent-text">"👤 상단 출발 참가자"</b>
                            <button class="btn xs" on:click=open_bulk_players>"이름 일괄 입력"</button>
                        </div>
                        <div class="entry-grid">{player_inputs}</div>
                    </div>
                    <div class="connector"><span>"▼ 사다리 다리로 무작위 연결 ▼"</span></div>
                    <div class="subcard green">
                        <div class="row between mb8">
                            <b class="ok">"🎁 하단 도착 칸"</b>
                            <div class="row gap4">
                                <button class="btn xs warn-text" on:click=move |_| {
                                    st.update(|s| util::shuffle(&mut s.results));
                                    l.rows_rev.update(|v| *v += 1);
                                    util::blur_active();
                                }>"🔀 위치 섞기"</button>
                                <button class="btn xs ok" on:click=open_bulk_results>"일괄 편집"</button>
                            </div>
                        </div>
                        <div class="row wrap gap4 mb8">
                            {[("🎉 당첨 1명", "🎉 당첨!", "꽝"), ("💣 벌칙 1명", "💣 벌칙!", "통과"), ("☕ 커피 쏘기", "☕ 커피 쏘기", "잘 마실게요")]
                                .into_iter().map(|(label, hit, miss)| view! {
                                    <button class="btn xs" on:click=move |_| {
                                        let n = st.with_untracked(|s| s.count);
                                        l.apply_results((0..n).map(|i| if i == 0 { hit.to_string() } else { miss.to_string() }).collect());
                                    }>{label}</button>
                                }).collect_view()}
                            <button class="btn xs" on:click=move |_| {
                                let n = st.with_untracked(|s| s.count);
                                l.apply_results((1..=n).map(|i| format!("{}등", i)).collect());
                            }>"🏆 1~N등"</button>
                        </div>
                        <div class="entry-grid">{result_inputs}</div>
                    </div>
                </div>
            </section>

            <section class="card col">
                <div class="card-head">
                    <h2>"┣┫ 사다리 보드"</h2>
                    <div class="row gap6">
                        <button class="btn sm" class:primary=move || st.with(|s| s.hide)
                            on:click=move |_| { st.update(|s| s.hide = !s.hide); util::blur_active(); }>
                            {move || if st.with(|s| s.hide) { "다리 가리기: ON" } else { "다리 가리기: OFF" }}
                        </button>
                        <button class="btn sm" on:click=move |_| { st.update(|s| s.dense = !s.dense); l.regenerate(); util::blur_active(); }>
                            {move || if st.with(|s| s.dense) { "다리 수: 많음" } else { "다리 수: 보통" }}
                        </button>
                        <button class="btn sm accent-text" on:click=move |_| { l.regenerate(); util::blur_active(); }>"🔄 사다리 재구성"</button>
                    </div>
                </div>
                <div class="status-bar accent-text ellipsis">{move || st.with(|s| s.status.clone())}</div>
                <div class="canvas-wrap grow">
                    <canvas node_ref=canvas_ref on:mousemove=on_move on:mousedown=on_down on:mouseleave=on_leave
                        on:contextmenu=move |e| e.prevent_default()></canvas>
                </div>
                <div class="action-grid three mt10">
                    <button class="btn lg" class:success=move || !animating() class:alt=animating on:click=move |_| l.space()>{next_label}</button>
                    <button class="btn lg" class:primary=move || !st.with(|s| s.auto_seq) class:danger=move || st.with(|s| s.auto_seq)
                        on:click=move |_| l.toggle_auto()>
                        {move || if st.with(|s| s.auto_seq) { "⏹ 자동 출발 중지" } else { "⚡ 순서대로 자동 출발" }}
                    </button>
                    <button class="btn lg" on:click=move |_| l.reveal_all()>"⏩ 전체 결과 보기"</button>
                </div>
            </section>

            <section class="card col">
                <div class="card-head">
                    <h2>"사다리 매칭 결과"</h2>
                    <button class="btn sm" on:click=move |_| { l.reset_traces(); util::blur_active(); }>"경로 지우기"</button>
                </div>
                <div class="list grow">{match_list}</div>
                <div class="ok small right mt8">{move || l.copy_fb.get()}</div>
                <button class="btn lg ok-text mt4" on:click=move |_| l.copy()>"📋 사다리 결과 복사하기"</button>
            </section>
        </div>
    }
}
