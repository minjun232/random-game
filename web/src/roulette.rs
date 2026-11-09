// 탭 0: 연속 룰렛 (연속 / 선승제 / 단판 모드)
use crate::util::{self, Sfx};
use crate::{tab_accepts_keys, use_modal, ActiveTab};
use leptos::*;

#[derive(Clone)]
struct Item {
    name: String,
    enabled: bool,
}

struct Preset {
    name: String,
    items: Vec<Item>,
}

#[derive(Clone)]
struct Slice {
    name: String,
    color: &'static str,
}

#[derive(Clone)]
struct Rec {
    spin_no: usize,
    item: String,
    color: &'static str,
    count: u32,  // 연속: 연속 횟수 / 선승제: 해당 항목 누적 승수
    target: u32, // 연속: 목표 연속 / 선승제: 필요 승수
    changed: bool,
    hit: bool,
    single: bool,
    best_of: bool,
    game_no: u32,
    time: String,
}

#[derive(Clone)]
enum Feed {
    Spin(Rec),
    Divider(u32),
}

const MODE_STREAK: usize = 0;
const MODE_BEST_OF: usize = 1;
const MODE_SINGLE: usize = 2;

struct RState {
    presets: Vec<Preset>,
    sel: usize,
    target_streak: u32,
    best_of_wins: u32,
    speed: usize,
    split_small: bool,
    auto_memo: bool,
    auto_new_round: bool,
    mode: usize,
    spinning: bool,
    auto: bool,
    feed: Vec<Feed>,
    history: Vec<Rec>,
    counts: Vec<(String, u32)>,
    cand: Option<String>,
    cand_color: &'static str,
    streak: u32,
    round: u32,
    won_pending: bool,
    last_switch: bool,
    match_wins: Vec<(String, u32)>,
    game_no: u32,
}

impl RState {
    fn new() -> Self {
        let mk = |name: &str, items: &[&str]| Preset {
            name: name.to_string(),
            items: items.iter().map(|n| Item { name: n.to_string(), enabled: true }).collect(),
        };
        RState {
            presets: vec![
                mk("없음", &[]),
                mk("오늘 메뉴 고르기", &["치킨", "피자", "삼겹살", "마라탕", "초밥", "국밥"]),
                mk("할까 말까 (2지선다)", &["한다!", "안 한다!"]),
                mk("숫자 뽑기 (1~5)", &["1번", "2번", "3번", "4번", "5번"]),
            ],
            sel: 0,
            target_streak: 3,
            best_of_wins: 2,
            speed: 1,
            split_small: true,
            auto_memo: true,
            auto_new_round: true,
            mode: MODE_STREAK,
            spinning: false,
            auto: false,
            feed: Vec::new(),
            history: Vec::new(),
            counts: Vec::new(),
            cand: None,
            cand_color: "#3A3D46",
            streak: 0,
            round: 1,
            won_pending: false,
            last_switch: false,
            match_wins: Vec::new(),
            game_no: 0,
        }
    }

    fn items(&self) -> &Vec<Item> {
        &self.presets[self.sel].items
    }

    fn items_mut(&mut self) -> &mut Vec<Item> {
        &mut self.presets[self.sel].items
    }

    fn count_of(&self, name: &str) -> u32 {
        self.counts.iter().find(|(n, _)| n == name).map(|(_, c)| *c).unwrap_or(0)
    }

    fn color_for(&self, name: &str) -> &'static str {
        self.items()
            .iter()
            .position(|i| i.name == name)
            .map(util::color)
            .unwrap_or(util::color(0))
    }

    fn clear_round_state(&mut self) {
        self.cand = None;
        self.streak = 0;
        self.won_pending = false;
        self.last_switch = false;
        self.match_wins.clear();
        self.game_no = 0;
    }

    fn auto_idle_label(&self) -> String {
        if self.mode == MODE_BEST_OF {
            format!("⚡ {}선승까지 자동", self.best_of_wins)
        } else {
            format!("⚡ {}연속까지 자동", self.target_streak)
        }
    }
}

/// 애니메이션용 비반응형 상태 (매 프레임 바뀌므로 화면 갱신을 유발하지 않게 분리)
struct Wheel {
    slices: Vec<Slice>,
    rot: f64,
    kick: f64,
    win_hl: bool,
    spinning: bool,
    start: f64,
    target: f64,
    dur: f64,
    t0: f64,
    last_idx: i32,
    planned: i32,
    loop_running: bool,
}

impl Wheel {
    fn current_index(&self) -> i32 {
        let n = self.slices.len();
        if n == 0 {
            return -1;
        }
        let deg = 360.0 / n as f64;
        let rel = ((-self.rot) % 360.0 + 360.0) % 360.0;
        ((rel / deg).floor() as i32).clamp(0, n as i32 - 1)
    }
}

#[derive(Clone, Copy)]
struct R {
    st: RwSignal<RState>,
    wheel: StoredValue<Wheel>,
    canvas: NodeRef<html::Canvas>,
    memo: RwSignal<String>,
    memo_fb: RwSignal<String>,
    memo_el: NodeRef<html::Textarea>,
}

fn duration_for(mode: usize) -> f64 {
    match mode {
        0 => 2.05,
        1 => 0.72,
        2 => 0.24,
        3 => 0.0,
        _ => 0.72,
    }
}

impl R {
    fn rebuild_slices(self) {
        let slices = self.st.with_untracked(|s| {
            let base: Vec<Slice> = s
                .items()
                .iter()
                .enumerate()
                .filter(|(_, it)| it.enabled && !it.name.trim().is_empty())
                .map(|(i, it)| Slice { name: it.name.clone(), color: util::color(i) })
                .collect();
            if s.split_small && (base.len() == 2 || base.len() == 3) {
                let repeat = if base.len() == 2 { 3 } else { 2 };
                (0..repeat).flat_map(|_| base.clone()).collect()
            } else {
                base
            }
        });
        self.wheel.update_value(|w| {
            w.slices = slices;
            w.win_hl = false;
        });
        self.draw();
    }

    fn draw(self) {
        if let Some(c) = self.canvas.get_untracked() {
            self.wheel.with_value(|w| draw_wheel(&c, w));
        }
    }

    fn ensure_loop(self) {
        let start = self.wheel.try_update_value(|w| {
            let s = !w.loop_running;
            w.loop_running = true;
            s
        });
        if start == Some(true) {
            request_animation_frame(move || self.frame());
        }
    }

    fn frame(self) {
        let mut finished = false;
        let mut tick = false;
        let keep = self
            .wheel
            .try_update_value(|w| {
                if w.kick.abs() > 0.1 {
                    w.kick *= 0.80;
                    if w.kick.abs() <= 0.1 {
                        w.kick = 0.0;
                    }
                }
                if w.spinning {
                    let t = (util::now_ms() - w.t0) / 1000.0 / w.dur;
                    if t >= 1.0 {
                        finished = true;
                    } else {
                        let inv = 1.0 - t;
                        let ease = 1.0 - inv * inv * inv * inv;
                        w.rot = w.start + (w.target - w.start) * ease;
                        let idx = w.current_index();
                        if idx != w.last_idx && idx >= 0 {
                            w.last_idx = idx;
                            w.kick = -16.0;
                            tick = true;
                        }
                    }
                }
                let keep = w.spinning || w.kick.abs() > 0.1;
                if !keep {
                    w.loop_running = false;
                }
                keep
            })
            .unwrap_or(false);
        if tick {
            util::play(Sfx::Tick);
        }
        if finished {
            self.finish_spin();
        }
        self.draw();
        if keep || finished {
            request_animation_frame(move || self.frame());
        } else {
            self.wheel.update_value(|w| w.loop_running = false);
        }
    }

    fn on_spin_click(self) {
        util::blur_active();
        if self.wheel.with_value(|w| w.spinning) {
            self.finish_spin();
            self.draw();
            return;
        }
        self.start_spin();
    }

    fn start_spin(self) {
        let n = self.wheel.with_value(|w| w.slices.len());
        if n == 0 {
            util::alert("왼쪽 목록에서 최소 1개 이상의 항목을 활성화해 주세요.");
            self.stop_auto();
            return;
        }
        let need_new_round = self.st.with_untracked(|s| {
            s.won_pending && (s.mode == MODE_BEST_OF || (s.mode == MODE_STREAK && s.auto_new_round))
        });
        if need_new_round {
            self.prepare_next_round();
        }

        let chosen = util::rand_int(n);
        let slice_deg = 360.0 / n as f64;
        let inside = 0.16 + util::rand_f64() * 0.68;
        let desired_rel = (chosen as f64 + inside) * slice_deg;
        let desired_mod = ((360.0 - desired_rel) % 360.0 + 360.0) % 360.0;

        let (speed, auto) = self.st.with_untracked(|s| (s.speed, s.auto));
        let mut dur = duration_for(speed);
        if auto && dur > 0.65 {
            dur = 0.55;
        }

        let instant = dur <= 0.01;
        self.wheel.update_value(|w| {
            let cur = ((w.rot % 360.0) + 360.0) % 360.0;
            let mut fwd = desired_mod - cur;
            if fwd <= 0.0 {
                fwd += 360.0;
            }
            w.planned = chosen as i32;
            if instant {
                w.rot += 360.0 + fwd;
                w.kick = 0.0;
            } else {
                let extra = match speed {
                    0 => 5.0,
                    1 => 3.0,
                    _ => 2.0,
                };
                w.start = w.rot;
                w.target = w.rot + extra * 360.0 + fwd;
                w.dur = dur;
                w.win_hl = false;
                w.last_idx = w.current_index();
                w.spinning = true;
                w.t0 = util::now_ms();
            }
        });

        if instant {
            util::play(Sfx::Tick);
            self.complete(chosen);
            self.draw();
        } else {
            self.st.update(|s| s.spinning = true);
            self.ensure_loop();
        }
    }

    fn finish_spin(self) {
        let idx = self.wheel.try_update_value(|w| {
            w.spinning = false;
            w.rot = w.target % 36000.0;
            w.kick = -8.0;
            let n = w.slices.len() as i32;
            if w.planned >= 0 && w.planned < n {
                w.planned
            } else {
                w.current_index()
            }
        });
        self.st.update(|s| s.spinning = false);
        self.ensure_loop();
        if let Some(i) = idx {
            if i >= 0 {
                self.complete(i as usize);
            }
        }
    }

    fn complete(self, slice_idx: usize) {
        let Some(winner) = self.wheel.with_value(|w| w.slices.get(slice_idx).cloned()) else { return };
        self.wheel.update_value(|w| w.win_hl = true);
        let name = winner.name.clone();
        let color = winner.color;

        let mut memo_line: Option<String> = None;
        let mut sound: Option<Sfx> = None;
        let mut auto_next: Option<bool> = None; // Some(true)=다음 자동 스핀, Some(false)=자동 중지

        self.st.update(|s| {
            match s.counts.iter_mut().find(|(n, _)| *n == name) {
                Some(e) => e.1 += 1,
                None => s.counts.push((name.clone(), 1)),
            }
            let preset = s.presets[s.sel].name.clone();
            let spin_no = s.history.len() + 1;
            let time = util::hhmmss();

            if s.mode == MODE_SINGLE {
                s.cand = Some(name.clone());
                s.cand_color = color;
                s.streak = 1;
                s.last_switch = false;
                let rec = Rec {
                    spin_no, item: name.clone(), color, count: 1, target: 1, changed: false,
                    hit: true, single: true, best_of: false, game_no: 0, time: time.clone(),
                };
                if s.auto_memo {
                    memo_line = Some(format!("[{}] 🎯 단판 당첨: {} ({}, #{}회차)", time, name, preset, spin_no));
                }
                s.history.push(rec.clone());
                s.feed.push(Feed::Spin(rec));
                sound = Some(Sfx::Win);
                return;
            }

            if s.mode == MODE_BEST_OF {
                let need = s.best_of_wins;
                s.game_no += 1;
                let wins = match s.match_wins.iter_mut().find(|(n, _)| *n == name) {
                    Some(e) => {
                        e.1 += 1;
                        e.1
                    }
                    None => {
                        s.match_wins.push((name.clone(), 1));
                        1
                    }
                };
                let max_w = s.match_wins.iter().map(|(_, w)| *w).max().unwrap_or(0);
                // 선두 = 최다 승수, 동률이면 방금 뽑힌 항목
                if wins == max_w || s.cand.is_none() {
                    s.last_switch = s.cand.as_deref().is_some_and(|c| c != name);
                    s.cand = Some(name.clone());
                    s.cand_color = color;
                } else {
                    s.last_switch = false;
                }
                s.streak = max_w;
                let won = wins >= need;
                if won {
                    s.won_pending = true;
                }
                let rec = Rec {
                    spin_no, item: name.clone(), color, count: wins, target: need, changed: false,
                    hit: won, single: false, best_of: true, game_no: s.game_no, time: time.clone(),
                };
                if won && s.auto_memo {
                    memo_line = Some(format!(
                        "[{}] 🏆 {}선승 당첨: {} ({}, {}판 만에 결정, #{}회차)",
                        time, need, name, preset, s.game_no, spin_no
                    ));
                }
                s.history.push(rec.clone());
                s.feed.push(Feed::Spin(rec));
                sound = if won {
                    Some(Sfx::Win)
                } else if wins >= 2 && wins + 1 == need {
                    Some(Sfx::Reach)
                } else {
                    None
                };
                if s.auto {
                    auto_next = Some(!won);
                }
                return;
            }

            // 연속 모드
            let had_prev = s.cand.is_some();
            if had_prev && s.cand.as_deref() == Some(name.as_str()) {
                s.streak += 1;
                s.last_switch = false;
            } else {
                s.cand = Some(name.clone());
                s.cand_color = color;
                s.streak = 1;
                s.last_switch = had_prev;
            }
            let target = s.target_streak;
            let hit = s.streak >= target;
            if hit {
                s.won_pending = true;
            }
            let rec = Rec {
                spin_no, item: name.clone(), color, count: s.streak, target, changed: s.last_switch,
                hit, single: false, best_of: false, game_no: 0, time: time.clone(),
            };
            if s.streak == target && s.auto_memo {
                memo_line = Some(format!("[{}] 🏆 {}연속 당첨: {} ({}, #{}회차)", time, s.streak, name, preset, spin_no));
            }
            s.history.push(rec.clone());
            s.feed.push(Feed::Spin(rec));
            sound = if hit {
                Some(Sfx::Win)
            } else if s.streak >= 2 && s.streak + 1 == target {
                Some(Sfx::Reach)
            } else {
                None
            };
            if s.auto {
                auto_next = Some(!hit);
            }
        });

        if let Some(sfx) = sound {
            util::play(sfx);
        }
        if let Some(line) = memo_line {
            self.append_memo(&line);
        }
        match auto_next {
            Some(true) => {
                set_timeout(
                    move || {
                        let go = self.st.with_untracked(|s| s.auto) && !self.wheel.with_value(|w| w.spinning);
                        if go {
                            self.start_spin();
                        }
                    },
                    std::time::Duration::from_millis(280),
                );
            }
            Some(false) => self.stop_auto(),
            None => {}
        }
    }

    fn toggle_auto(self) {
        util::blur_active();
        let (mode, auto, won) = self.st.with_untracked(|s| (s.mode, s.auto, s.won_pending));
        if mode == MODE_SINGLE {
            return;
        }
        if auto {
            self.stop_auto();
            return;
        }
        if self.wheel.with_value(|w| w.slices.is_empty()) {
            util::alert("돌릴 수 있는 활성 항목이 없습니다.");
            return;
        }
        if won {
            self.prepare_next_round();
        }
        self.st.update(|s| s.auto = true);
        if !self.wheel.with_value(|w| w.spinning) {
            self.start_spin();
        }
    }

    fn stop_auto(self) {
        if self.st.with_untracked(|s| s.auto) {
            self.st.update(|s| s.auto = false);
        }
    }

    fn prepare_next_round(self) {
        self.st.update(|s| {
            s.round += 1;
            s.clear_round_state();
            if !s.history.is_empty() {
                s.feed.push(Feed::Divider(s.round));
            }
        });
    }

    fn reset_streak_only(self) {
        self.stop_auto();
        self.st.update(|s| s.clear_round_state());
        self.wheel.update_value(|w| w.win_hl = false);
        self.draw();
    }

    fn set_mode(self, mode: usize) {
        if self.st.with_untracked(|s| s.mode) == mode {
            return;
        }
        self.stop_auto();
        self.st.update(|s| {
            s.mode = mode;
            s.clear_round_state();
        });
        self.wheel.update_value(|w| w.win_hl = false);
        self.draw();
    }

    fn clear_history(self) {
        self.stop_auto();
        self.st.update(|s| {
            s.history.clear();
            s.feed.clear();
            s.counts.clear();
            s.round = 1;
            s.clear_round_state();
        });
    }

    fn items_changed(self) {
        self.rebuild_slices();
    }

    fn append_memo(self, line: &str) {
        self.memo.update(|m| {
            if !m.is_empty() && !m.ends_with('\n') {
                m.push('\n');
            }
            m.push_str(line);
            m.push('\n');
        });
        self.memo_fb.set(String::new());
        let el = self.memo_el;
        request_animation_frame(move || {
            if let Some(m) = el.get_untracked() {
                m.set_scroll_top(m.scroll_height());
            }
        });
    }

    fn manual_memo_line(rec: &Rec) -> String {
        let tag = if rec.single {
            "🎯 단판 당첨".to_string()
        } else if rec.best_of {
            if rec.hit {
                format!("🏆 {}선승 확정", rec.target)
            } else {
                format!("{}판째 · {}승", rec.game_no, rec.count)
            }
        } else if rec.hit {
            format!("🏆 {}연속 확정", rec.count)
        } else {
            format!("{}연속", rec.count)
        };
        format!("[{}] #{} {} ({})", rec.time, rec.spin_no, rec.item, tag)
    }
}

// ---------------------------------------------------------------------------
// 휠 그리기 (평면형 디자인)
// ---------------------------------------------------------------------------
fn draw_wheel(canvas: &web_sys::HtmlCanvasElement, w: &Wheel) {
    let Some((cw, ch, ctx)) = util::prepare_canvas(canvas) else { return };
    use std::f64::consts::PI;
    let rad = |d: f64| d * PI / 180.0;
    let cx = cw / 2.0;
    let cy = ch / 2.0 + 8.0;
    let radius = ((cw.min(ch)) / 2.0 - 26.0).max(60.0);

    // 바깥 링
    ctx.begin_path();
    let _ = ctx.arc(cx, cy, radius + 10.0, 0.0, PI * 2.0);
    ctx.set_fill_style_str("#202229");
    ctx.fill();
    ctx.set_line_width(2.0);
    ctx.set_stroke_style_str(if w.win_hl { "#3DB47F" } else { "#30333D" });
    ctx.stroke();

    let font = |size: f64| format!("700 {}px {}", size, util::FONT_STACK);
    ctx.set_text_align("center");
    ctx.set_text_baseline("middle");

    let n = w.slices.len();
    if n == 0 {
        ctx.begin_path();
        let _ = ctx.arc(cx, cy, radius, 0.0, PI * 2.0);
        ctx.set_fill_style_str("#1A1B20");
        ctx.fill();
        ctx.set_font(&font(15.0));
        ctx.set_fill_style_str("#A0A3AC");
        let _ = ctx.fill_text("왼쪽에서 항목을 추가하세요", cx, cy);
        return;
    }

    let slice_deg = 360.0 / n as f64;
    let active = w.current_index();

    if n == 1 {
        ctx.begin_path();
        let _ = ctx.arc(cx, cy, radius, 0.0, PI * 2.0);
        ctx.set_fill_style_str(w.slices[0].color);
        ctx.fill();
        ctx.set_font(&font(20.0));
        ctx.set_fill_style_str("#FFFFFF");
        let _ = ctx.fill_text(&w.slices[0].name, cx, cy - radius * 0.55);
    } else {
        for (i, sl) in w.slices.iter().enumerate() {
            let start = w.rot - 90.0 + i as f64 * slice_deg;
            let is_win = w.win_hl && i as i32 == active;
            ctx.begin_path();
            ctx.move_to(cx, cy);
            let _ = ctx.arc(cx, cy, radius, rad(start), rad(start + slice_deg));
            ctx.close_path();
            if is_win {
                ctx.set_fill_style_str(&util::lighten(sl.color, 0.22));
            } else {
                ctx.set_fill_style_str(sl.color);
            }
            ctx.fill();
            ctx.set_line_width(2.0);
            ctx.set_stroke_style_str("rgba(17,18,22,0.85)");
            ctx.stroke();
            if is_win {
                ctx.set_line_width(3.5);
                ctx.set_stroke_style_str("#FFE9A3");
                ctx.stroke();
            }

            // 칸 이름
            let mid = start + slice_deg / 2.0;
            let label = util::truncate_chars(&sl.name, 11);
            let mut size = 15.0;
            if n > 10 {
                size = 12.5;
            }
            if n > 16 {
                size = 10.5;
            }
            if label.chars().count() > 7 && size > 12.5 {
                size = 12.5;
            }
            let norm = ((mid % 360.0) + 360.0) % 360.0;
            let flip = norm > 90.0 && norm < 270.0;
            let tr = radius * 0.63;
            ctx.save();
            let _ = ctx.translate(cx + tr * rad(mid).cos(), cy + tr * rad(mid).sin());
            let _ = ctx.rotate(rad(if flip { mid + 180.0 } else { mid }));
            ctx.set_font(&font(size));
            ctx.set_fill_style_str("rgba(10,11,14,0.55)");
            let _ = ctx.fill_text(&label, 1.0, 1.0);
            ctx.set_fill_style_str("#FFFFFF");
            let _ = ctx.fill_text(&label, 0.0, 0.0);
            ctx.restore();
        }
    }

    // 가운데 허브 (현재 바늘 아래 칸 표시)
    let hub = (radius * 0.23).max(36.0);
    ctx.begin_path();
    let _ = ctx.arc(cx, cy, hub, 0.0, PI * 2.0);
    ctx.set_fill_style_str("#16171C");
    ctx.fill();
    ctx.set_line_width(3.0);
    ctx.set_stroke_style_str(if w.win_hl { "#3DB47F" } else { "#5B6CF0" });
    ctx.stroke();
    if active >= 0 {
        let name = util::truncate_chars(&w.slices[active as usize].name, 7);
        ctx.set_font(&font((hub * 0.30).max(11.5)));
        ctx.set_fill_style_str("#FFFFFF");
        let _ = ctx.fill_text(&name, cx, cy);
    }

    // 12시 방향 바늘 (튕김 효과)
    let px = cx;
    let py = cy - radius - 12.0;
    ctx.save();
    let _ = ctx.translate(px, py);
    let _ = ctx.rotate(rad(w.kick));
    ctx.begin_path();
    ctx.move_to(0.0, 30.0);
    ctx.line_to(-12.0, -6.0);
    ctx.line_to(12.0, -6.0);
    ctx.close_path();
    ctx.set_fill_style_str("#F4F4F6");
    ctx.fill();
    ctx.set_line_width(2.0);
    ctx.set_stroke_style_str("#111216");
    ctx.stroke();
    ctx.begin_path();
    let _ = ctx.arc(0.0, 0.0, 5.0, 0.0, PI * 2.0);
    ctx.set_fill_style_str("#5B6CF0");
    ctx.fill();
    ctx.restore();
}

// ---------------------------------------------------------------------------
// 전광판 계산
// ---------------------------------------------------------------------------
#[derive(Clone, Copy, PartialEq)]
enum Lamp {
    Off,
    Lit,
    Hot,
    Win,
}

struct Board {
    tone: &'static str, // "", "hot", "win"
    caption: &'static str,
    dot: &'static str,
    name: String,
    name_tone: &'static str,
    tag: Option<(String, &'static str)>,
    lamps: Vec<Lamp>,
    status: String,
    status_tone: &'static str,
}

fn board_for(s: &RState) -> Board {
    let waiting = |caption: &'static str, status: String| Board {
        tone: "",
        caption,
        dot: "#3A3D46",
        name: "대기 중".into(),
        name_tone: "muted",
        tag: None,
        lamps: vec![],
        status,
        status_tone: "muted",
    };
    let cand = s.cand.clone().unwrap_or_default();

    if s.mode == MODE_SINGLE {
        if s.cand.is_none() || s.streak == 0 {
            return waiting("이번 단판 당첨 결과", "단판 모드 — 1번 돌린 결과가 바로 당첨됩니다!".into());
        }
        return Board {
            tone: "win",
            caption: "이번 단판 당첨 결과",
            dot: s.cand_color,
            name: cand.clone(),
            name_tone: "win",
            tag: Some(("🎯 단판 당첨!".into(), "win")),
            lamps: vec![Lamp::Win],
            status: format!("🎯 단판 결과 확정: [{}] 당첨!", cand),
            status_tone: "win",
        };
    }

    if s.mode == MODE_BEST_OF {
        // v1.2 UpdateBestOfScoreboardUI와 동일한 규칙
        let need = s.best_of_wins;
        let lead = s.streak;
        let won = s.won_pending && lead >= need;
        let hot = !won && lead > 0 && lead + 1 == need;
        let lamp_kind = if won {
            Lamp::Win
        } else if lead >= 2 {
            Lamp::Hot
        } else {
            Lamp::Lit
        };
        let lamps = (0..need).map(|i| if i < lead { lamp_kind } else { Lamp::Off }).collect();
        if s.game_no == 0 || s.cand.is_none() {
            let mut b = waiting("현재 선두", format!("{0}선승제 — 먼저 {0}번 뽑힌 항목이 승리합니다", need));
            b.lamps = lamps;
            return b;
        }
        let mut tally: Vec<(String, u32)> = s.match_wins.clone();
        tally.sort_by(|a, b| b.1.cmp(&a.1));
        let tally_txt = tally
            .iter()
            .take(5)
            .map(|(n, w)| format!("{} {}승", n, w))
            .collect::<Vec<_>>()
            .join(" · ");
        return Board {
            tone: if won { "win" } else if hot { "hot" } else { "" },
            caption: "현재 선두",
            dot: s.cand_color,
            name: cand.clone(),
            name_tone: if won { "win" } else { "" },
            tag: Some(if won {
                (format!("🏆 {}선승!", need), "win")
            } else {
                (format!("{}승 / {}승", lead, need), if hot { "hot" } else { "info" })
            }),
            lamps,
            status: if won {
                format!("🎉 [{}] {}선승 달성! ({}판 만에 결정)", cand, need, s.game_no)
            } else {
                format!("{}판 진행 · {}", s.game_no, tally_txt)
            },
            status_tone: if won { "win" } else if hot { "hot" } else { "" },
        };
    }

    // 연속 모드
    let target = s.target_streak;
    let streak = s.streak;
    let is_win = streak >= target && streak > 0;
    let is_building = !is_win && streak >= 2;
    let lamp_count = target.max(streak).min(6);
    let lamp_kind = if is_win {
        Lamp::Win
    } else if streak >= 2 {
        Lamp::Hot
    } else {
        Lamp::Lit
    };
    let lamps = (0..lamp_count).map(|i| if i < streak { lamp_kind } else { Lamp::Off }).collect();

    if streak == 0 || s.cand.is_none() {
        let mut b = waiting(
            "현재 연속 후보",
            format!("목표 [{}연속] 대기 중 — 스페이스바나 돌리기 버튼을 눌러주세요", target),
        );
        b.lamps = lamps;
        return b;
    }

    let tag = if is_win {
        (format!("🏆 {}연속 달성!", streak), "win")
    } else if streak >= 2 {
        (format!("🔥 {}연속 적중!", streak), "hot")
    } else if s.last_switch {
        ("후보 교체 · 1S".to_string(), "")
    } else {
        ("첫 진입 · 1S".to_string(), "info")
    };
    let prefix = if s.last_switch { "후보 교체 → " } else { "" };
    let (status, tone) = if is_win {
        (format!("🎉 삼진/당첨 확정!! [{}] 항목이 {}연속으로 뽑혔습니다!", cand, streak), "win")
    } else if streak + 1 == target {
        if streak == 1 && target == 2 {
            (format!("{}[{}] 1스트라이크! 다음 번에도 같으면 2연속 당첨 확정!", prefix, cand), "warn")
        } else {
            (format!("🔥 리치!! [{}] {}연속 적중! 마지막 1번만 더 나오면 확정!", cand, streak), "hot")
        }
    } else {
        (format!("{}[{}] {}스트라이크 ({} / {})", prefix, cand, streak, streak, target), "")
    };

    Board {
        tone: if is_win { "win" } else if is_building || streak + 1 == target { "hot" } else { "" },
        caption: "현재 연속 후보",
        dot: s.cand_color,
        name: cand,
        name_tone: if is_win { "win" } else { "" },
        tag: Some(tag),
        lamps,
        status,
        status_tone: tone,
    }
}

fn feed_row_view(r: R, rec: Rec) -> impl IntoView {
    let is_win = rec.hit;
    let is_multi = !is_win && !rec.single && rec.count >= 2;
    let tone = if is_win { "win" } else if is_multi { "hot" } else { "" };
    let dots: Vec<Lamp> = if rec.single {
        vec![]
    } else {
        let total = rec.target.max(rec.count).min(5);
        (0..total)
            .map(|d| {
                if d >= rec.count {
                    Lamp::Off
                } else if is_win {
                    Lamp::Win
                } else if is_multi {
                    Lamp::Hot
                } else {
                    Lamp::Lit
                }
            })
            .collect()
    };
    let badge = if rec.single {
        "🎯 당첨!".to_string()
    } else if rec.best_of {
        if is_win {
            format!("🏆 {}선승!", rec.target)
        } else {
            format!("{}판 · {}승", rec.game_no, rec.count)
        }
    } else if is_win {
        format!("🏆 {}연속!", rec.count)
    } else if is_multi {
        format!("🔥 {}연속", rec.count)
    } else if rec.changed {
        "1S·교체".into()
    } else {
        "1S".into()
    };
    let badge_tone = if rec.single || is_win { "win" } else if is_multi { "hot" } else { "" };
    let line = R::manual_memo_line(&rec);
    view! {
        <div class=format!("feed-row {}", tone)>
            <span class="mono muted">{format!("#{}", rec.spin_no)}</span>
            <span class="dot" style:background=rec.color></span>
            <span class="feed-name">{rec.item.clone()}</span>
            <span class="mini-dots">
                {dots.into_iter().map(|d| view! { <i class=lamp_class(d)></i> }).collect_view()}
            </span>
            <span class=format!("pill {}", badge_tone)>{badge}</span>
            <button class="btn xs ghost" title="이 결과를 메모장에 추가합니다"
                on:click=move |_| { r.append_memo(&line); util::blur_active(); }>"+메모"</button>
        </div>
    }
}

fn lamp_class(l: Lamp) -> &'static str {
    match l {
        Lamp::Off => "lamp off",
        Lamp::Lit => "lamp lit",
        Lamp::Hot => "lamp hot",
        Lamp::Win => "lamp win",
    }
}

#[component]
pub fn RouletteTab() -> impl IntoView {
    let r = R {
        st: create_rw_signal(RState::new()),
        wheel: store_value(Wheel {
            slices: vec![],
            rot: 0.0,
            kick: 0.0,
            win_hl: false,
            spinning: false,
            start: 0.0,
            target: 0.0,
            dur: 0.75,
            t0: 0.0,
            last_idx: -1,
            planned: -1,
            loop_running: false,
        }),
        canvas: create_node_ref::<html::Canvas>(),
        memo: create_rw_signal("=== 당첨 기록 & 메모 ===\n".to_string()),
        memo_fb: create_rw_signal(String::new()),
        memo_el: create_node_ref::<html::Textarea>(),
    };
    let modal = use_modal();
    let st = r.st;
    let new_item = create_node_ref::<html::Input>();
    let new_item_text = create_rw_signal(String::new());
    let feed_ref = create_node_ref::<html::Div>();
    let canvas_ref = r.canvas;
    let memo_el = r.memo_el;

    // 첫 그리기 & 창 크기/탭 전환 시 다시 그리기
    r.canvas.on_load(move |_| request_animation_frame(move || r.rebuild_slices()));
    window_event_listener(ev::resize, move |_| r.draw());
    let active = use_context::<ActiveTab>().expect("ActiveTab").0;
    create_effect(move |_| {
        if active.get() == 0 {
            request_animation_frame(move || r.draw());
        } else {
            r.stop_auto();
        }
    });

    // 기록/메모는 항상 맨 아래로 스크롤
    create_effect(move |_| {
        st.with(|s| s.feed.len());
        request_animation_frame(move || {
            if let Some(f) = feed_ref.get_untracked() {
                f.set_scroll_top(f.scroll_height());
            }
        });
    });

    // 단축키: Space 돌리기/즉시 멈추기, A 자동, R 리셋, Esc 자동 중지
    window_event_listener(ev::keydown, move |e: web_sys::KeyboardEvent| {
        if !tab_accepts_keys(0) || e.is_composing() {
            return;
        }
        let code = e.code();
        if util::focused_is_text_input() {
            // 빈 새 항목 입력창에서 Space → 바로 돌리기
            let in_new_item = new_item.get_untracked().is_some_and(|inp| {
                let el: &web_sys::Element = &inp;
                web_sys::window()
                    .and_then(|w| w.document())
                    .and_then(|d| d.active_element())
                    .is_some_and(|a| a == *el)
            });
            if code == "Space" && in_new_item && new_item_text.get_untracked().is_empty() && !e.repeat() {
                e.prevent_default();
                r.on_spin_click();
            }
            return;
        }
        match code.as_str() {
            "Space" => {
                e.prevent_default();
                if !e.repeat() {
                    r.on_spin_click();
                }
            }
            "KeyA" if !e.ctrl_key() && !e.meta_key() => {
                if st.with_untracked(|s| s.mode) != MODE_SINGLE {
                    r.toggle_auto();
                }
            }
            "KeyR" if !e.ctrl_key() && !e.meta_key() => r.reset_streak_only(),
            "Escape" => r.stop_auto(),
            _ => {}
        }
    });

    let add_item = move |refocus_wheel: bool| {
        let raw = new_item_text.get_untracked().trim().to_string();
        if raw.is_empty() {
            if refocus_wheel {
                util::blur_active();
            }
            return;
        }
        st.update(|s| s.items_mut().push(Item { name: raw, enabled: true }));
        new_item_text.set(String::new());
        r.items_changed();
        if refocus_wheel {
            util::blur_active();
        }
    };

    let open_bulk = move |_| {
        let text = st.with_untracked(|s| s.items().iter().map(|i| i.name.clone()).collect::<Vec<_>>().join("\n"));
        modal.open(
            "항목 일괄 텍스트 편집",
            "한 줄에 항목을 하나씩 입력하거나 붙여넣으세요:",
            text + "\n",
            move |res| {
                let lines = util::split_lines(&res, false);
                st.update(|s| {
                    *s.items_mut() = lines.into_iter().map(|n| Item { name: n, enabled: true }).collect();
                });
                r.items_changed();
            },
        );
    };

    let clear_items = move |_| {
        r.stop_auto();
        st.update(|s| {
            s.items_mut().clear();
            s.clear_round_state();
        });
        r.items_changed();
        util::blur_active();
    };

    let on_preset = move |e: web_sys::Event| {
        let idx: usize = event_target_value(&e).parse().unwrap_or(0);
        r.stop_auto();
        st.update(|s| {
            s.sel = idx.min(s.presets.len() - 1);
            if s.sel == 0 {
                s.items_mut().clear();
            }
            s.clear_round_state();
        });
        r.items_changed();
        util::blur_active();
    };

    // ------------------------------------------------------------------ 왼쪽: 항목
    let item_list = move || {
        st.with(|s| {
            let items = s.items();
            if items.is_empty() {
                return view! {
                    <div class="empty">"항목이 비어 있습니다."<br/>"위 입력창에 항목을 입력하고 Enter를 누르거나,"<br/>"프리셋을 선택해 보세요."</div>
                }
                .into_view();
            }
            items
                .iter()
                .enumerate()
                .map(|(i, it)| {
                    let name = it.name.clone();
                    let enabled = it.enabled;
                    let hits = s.count_of(&it.name);
                    let rename_from = name.clone();
                    view! {
                        <div class="item-row" class:disabled=!enabled>
                            <input type="checkbox" prop:checked=enabled on:change=move |e| {
                                let v = event_target_checked(&e);
                                st.update(|s| if let Some(it) = s.items_mut().get_mut(i) { it.enabled = v; });
                                r.items_changed();
                                util::blur_active();
                            }/>
                            <span class="dot" style:background=if enabled { util::color(i) } else { "#3A3D46" }></span>
                            <span class="item-name" title="더블클릭하여 이름 수정" on:dblclick=move |_| {
                                if let Some(ed) = util::prompt("새 이름을 입력하세요:", &rename_from) {
                                    let ed = ed.trim().to_string();
                                    if !ed.is_empty() {
                                        st.update(|s| if let Some(it) = s.items_mut().get_mut(i) { it.name = ed; });
                                        r.items_changed();
                                    }
                                }
                            }>{name}</span>
                            {(hits > 0).then(|| view! { <span class="pill">{format!("{}회", hits)}</span> })}
                            <button class="icon-btn" title="삭제" on:click=move |_| {
                                st.update(|s| { if i < s.items().len() { s.items_mut().remove(i); } });
                                r.items_changed();
                                util::blur_active();
                            }>"✕"</button>
                        </div>
                    }
                    .into_view()
                })
                .collect_view()
        })
    };

    let item_summary = move || {
        st.with(|s| {
            let items = s.items();
            if items.is_empty() {
                return "활성 항목: 0개 (위 입력창에서 바로 추가 가능)".to_string();
            }
            let n = items.iter().filter(|i| i.enabled).count();
            if n == 0 {
                "활성화된 항목이 없습니다 (최소 1개 이상 체크하세요)".into()
            } else {
                let pct = 100.0 / n as f64;
                let pct_txt = if (pct - pct.round()).abs() < 0.05 {
                    format!("{}", pct.round())
                } else {
                    format!("{:.1}", pct)
                };
                format!("활성 항목: {}개 (각 {}%) · 더블클릭 시 이름 수정", n, pct_txt)
            }
        })
    };

    // ------------------------------------------------------------------ 가운데: 전광판
    let mode_names = ["연속", "선승제", "단판"];
    let mode_tips = [
        "같은 항목이 목표 횟수만큼 연속으로 나오면 당첨",
        "먼저 정해진 승수를 채운 항목이 당첨 (2선승 ~ 5선승)",
        "1번 돌린 결과가 바로 당첨",
    ];
    let scoreboard = move || {
        st.with(|s| {
            let b = board_for(s);
            let tag = b.tag.clone();
            view! {
                <div class=format!("board {}", b.tone)>
                    <div class="board-main">
                        <div class="cand">
                            <div class="cand-top">
                                <span class="muted small">{b.caption}</span>
                                {tag.map(|(t, tone)| view! { <span class=format!("pill {}", tone)>{t}</span> })}
                            </div>
                            <div class="cand-row">
                                <span class="dot lg" style:background=b.dot></span>
                                <span class=format!("cand-name {}", b.name_tone)>{b.name.clone()}</span>
                            </div>
                        </div>
                        <div class="lamps">
                            <Show when=move || st.with(|s| s.mode != MODE_SINGLE)>
                                <span class="bso">{move || if st.with(|s| s.mode) == MODE_BEST_OF { "W" } else { "S" }}</span>
                            </Show>
                            {b.lamps.iter().map(|l| view! { <i class=lamp_class(*l)>{if *l == Lamp::Win { "★" } else { "" }}</i> }).collect_view()}
                        </div>
                    </div>
                    <div class=format!("board-status {}", b.status_tone)>{b.status.clone()}</div>
                </div>
            }
        })
    };

    let mode = move || st.with(|s| s.mode);

    let feed_view = move || {
        st.with(|s| {
            if s.feed.is_empty() {
                return view! { <div class="empty">"아직 돌린 기록이 없습니다."</div> }.into_view();
            }
            s.feed
                .iter()
                .map(|f| match f {
                    Feed::Spin(rec) => feed_row_view(r, rec.clone()).into_view(),
                    Feed::Divider(n) => view! { <div class="divider-row">{format!("── 라운드 {} (새 카운트 시작) ──", n)}</div> }.into_view(),
                })
                .collect_view()
        })
    };

    let freq_view = move || {
        st.with(|s| {
            if s.counts.is_empty() {
                return view! { <span class="muted small">"아직 돌린 기록이 없습니다 (항목별 누적 횟수가 표시됩니다)"</span> }.into_view();
            }
            let mut sorted = s.counts.clone();
            sorted.sort_by(|a, b| b.1.cmp(&a.1));
            sorted
                .into_iter()
                .map(|(n, c)| {
                    let col = s.color_for(&n);
                    view! { <span class="chip"><span class="dot sm" style:background=col></span>{format!("{} {}회", n, c)}</span> }
                })
                .collect_view()
        })
    };

    view! {
        <div class="cols roulette">
            // ===== 왼쪽: 항목 설정 =====
            <section class="card col">
                <div class="card-head">
                    <h2>"룰렛 항목 설정"</h2>
                    <div class="row gap4">
                        <button class="btn sm" on:click=open_bulk>"일괄 편집"</button>
                        <button class="btn sm danger-text" title="현재 목록의 항목을 모두 비웁니다" on:click=clear_items>"비우기"</button>
                    </div>
                </div>
                <label class="field-label">"기본 프리셋 불러오기"</label>
                <select class="input" on:change=on_preset prop:value=move || st.with(|s| s.sel.to_string())>
                    {st.with_untracked(|s| s.presets.iter().enumerate().map(|(i, p)| view! { <option value=i.to_string()>{p.name.clone()}</option> }).collect_view())}
                </select>
                <div class="row gap6 mt10">
                    <input class="input grow" node_ref=new_item placeholder="새 항목 입력 후 Enter..."
                        title="항목 이름을 입력하고 Enter 키를 누르세요 (비어 있을 때 Space를 누르면 바로 룰렛이 돌아갑니다)"
                        prop:value=new_item_text
                        on:input=move |e| new_item_text.set(event_target_value(&e))
                        on:keydown=move |e| { if e.key() == "Enter" && !e.is_composing() { e.prevent_default(); add_item(false); } }/>
                    <button class="btn primary" on:click=move |_| add_item(true)>"+ 추가"</button>
                </div>
                <div class="list grow mt10">{item_list}</div>
                <div class="muted small mt8">{item_summary}</div>
                <label class="check mt6" title="항목이 2개나 3개일 때 휠을 6칸으로 번갈아 나누어 회전감을 살립니다 (당첨 확률은 완전히 동일)">
                    <input type="checkbox" prop:checked=move || st.with(|s| s.split_small) on:change=move |e| {
                        let v = event_target_checked(&e);
                        st.update(|s| s.split_small = v);
                        r.rebuild_slices();
                        util::blur_active();
                    }/>
                    "항목 2~3개일 때 6칸 교차 분할 (확률 동일)"
                </label>
            </section>

            // ===== 가운데: 전광판 + 휠 + 조작 =====
            <section class="col center-col">
                <div class="card board-card">
                    <div class="card-head">
                        <h2>{move || match mode() { MODE_STREAK => "연속 스트라이크 전광판", MODE_BEST_OF => "선승제 전광판", _ => "단판 룰렛 전광판" }}</h2>
                        <div class="row gap6">
                            <div class="seg">
                                {mode_names.iter().enumerate().map(|(i, n)| view! {
                                    <button class="seg-btn" class:active=move || mode() == i title=mode_tips[i]
                                        on:click=move |_| { r.set_mode(i); util::blur_active(); }>{*n}</button>
                                }).collect_view()}
                            </div>
                            <Show when=move || mode() != MODE_SINGLE>
                                <button class="btn sm" title=move || if mode() == MODE_BEST_OF { "현재 경기의 승수를 0으로 초기화합니다 (단축키: R)" } else { "현재 연속 카운트만 0으로 초기화합니다 (단축키: R)" }
                                    on:click=move |_| { r.reset_streak_only(); util::blur_active(); }>"카운트 리셋 (R)"</button>
                            </Show>
                        </div>
                    </div>
                    <Show when=move || mode() != MODE_SINGLE>
                        <div class="row between mb8">
                            <div class="row gap4">
                                <span class="muted small">{move || if mode() == MODE_BEST_OF { "경기 방식:" } else { "목표 연속:" }}</span>
                                <Show when=move || mode() == MODE_STREAK>
                                    {[2u32, 3, 4, 5].into_iter().map(|t| view! {
                                        <button class="pill-btn" class:active=move || st.with(|s| s.target_streak == t)
                                            on:click=move |_| { st.update(|s| s.target_streak = t); util::blur_active(); }>{format!("{}연속", t)}</button>
                                    }).collect_view()}
                                </Show>
                                <Show when=move || mode() == MODE_BEST_OF>
                                    {[2u32, 3, 4, 5].into_iter().map(|w| view! {
                                        <button class="pill-btn" class:active=move || st.with(|s| s.best_of_wins == w)
                                            on:click=move |_| {
                                                st.update(|s| s.best_of_wins = w);
                                                r.reset_streak_only();
                                                util::blur_active();
                                            }>{format!("{}선승", w)}</button>
                                    }).collect_view()}
                                </Show>
                            </div>
                            <span class="pill info">{move || format!("라운드 {}", st.with(|s| s.round))}</span>
                        </div>
                    </Show>
                    {scoreboard}
                </div>
                <div class="card wheel-card">
                    <canvas node_ref=canvas_ref class="wheel-canvas"></canvas>
                </div>
                <div class="card ctrl-card">
                    <div class="row between mb10">
                        <div class="row gap4">
                            <span class="muted small">"속도:"</span>
                            {[("일반", "일반 회전 속도 (약 2.0초)"), ("빠름", "빠른 회전 속도 (약 0.7초, 다회차 추천)"), ("초고속", "초고속 회전 (약 0.25초)"), ("즉시", "회전 애니메이션 없이 즉시 결과")]
                                .into_iter().enumerate().map(|(i, (n, tip))| view! {
                                    <button class="pill-btn" title=tip class:active=move || st.with(|s| s.speed == i)
                                        on:click=move |_| { st.update(|s| s.speed = i); util::blur_active(); }>{n}</button>
                                }).collect_view()}
                        </div>
                        <label class="check">
                            <input type="checkbox" prop:checked=util::sound_enabled() on:change=move |e| {
                                util::set_sound_enabled(event_target_checked(&e));
                                util::blur_active();
                            }/>
                            "효과음"
                        </label>
                    </div>
                    <div class="action-grid">
                        <button class="btn primary lg" class:alt=move || st.with(|s| s.spinning)
                            on:click=move |_| r.on_spin_click()>
                            {move || if st.with(|s| s.spinning) { "⏩ 즉시 멈추기 (Space)" } else { "▶ 돌리기 (Space)" }}
                        </button>
                        <button class="btn lg" class:success=move || mode() != MODE_SINGLE && !st.with(|s| s.auto)
                            class:danger=move || st.with(|s| s.auto)
                            disabled=move || mode() == MODE_SINGLE
                            title=move || if mode() == MODE_SINGLE { "단판 모드에서는 자동 돌리기를 쓸 수 없습니다" } else { "" }
                            on:click=move |_| r.toggle_auto()>
                            {move || st.with(|s| if s.mode == MODE_SINGLE { "연속·선승제 전용".to_string() } else if s.auto { "⏹ 자동 중지 (Esc)".to_string() } else { s.auto_idle_label() })}
                        </button>
                    </div>
                    <div class="muted xs center mt6">"단축키: [Space] 돌리기/즉시스킵 · [A] 자동 · [R] 카운트리셋 · [Esc] 중지/입력창나오기"</div>
                </div>
            </section>

            // ===== 오른쪽: 기록 + 메모 =====
            <section class="col right-col">
                <div class="card hist-card">
                    <div class="card-head">
                        <div class="row gap6">
                            <h2>{move || match mode() { MODE_STREAK => "스트라이크 기록", MODE_BEST_OF => "선승제 기록", _ => "스핀 기록" }}</h2>
                            <span class="pill accent">{move || format!("{}회", st.with(|s| s.history.len()))}</span>
                        </div>
                        <div class="row gap4">
                            <button class="btn sm" title="새로운 주제를 돌릴 때 기록에 라운드 구분선을 넣습니다"
                                on:click=move |_| { r.stop_auto(); r.prepare_next_round(); util::blur_active(); }>"구분선"</button>
                            <button class="btn sm danger-text" title="스핀 기록과 누적 횟수를 비웁니다"
                                on:click=move |_| { r.clear_history(); util::blur_active(); }>"비우기"</button>
                        </div>
                    </div>
                    <div class="freq">{freq_view}</div>
                    <div class="list grow feed" node_ref=feed_ref>{feed_view}</div>
                </div>
                <div class="card memo-card">
                    <div class="card-head">
                        <h2>"결과 & 자유 메모장"</h2>
                        <span class="ok small">{move || r.memo_fb.get()}</span>
                    </div>
                    <div class="row wrap gap10 mb6">
                        <label class="check">
                            <input type="checkbox" prop:checked=move || st.with(|s| s.auto_memo)
                                on:change=move |e| { let v = event_target_checked(&e); st.update(|s| s.auto_memo = v); }/>
                            "당첨 확정 시 자동 기록"
                        </label>
                        <Show when=move || mode() == MODE_STREAK>
                            <label class="check" title="목표 연속 당첨 이후 다시 돌리면 카운트를 1스트라이크부터 새로 시작합니다">
                                <input type="checkbox" prop:checked=move || st.with(|s| s.auto_new_round)
                                    on:change=move |e| { let v = event_target_checked(&e); st.update(|s| s.auto_new_round = v); }/>
                                "달성 후 다음 스핀 시 새 라운드"
                            </label>
                        </Show>
                    </div>
                    <div class="row wrap gap4 mb8">
                        <button class="btn sm primary" on:click=move |_| {
                            let last = st.with_untracked(|s| s.history.last().cloned());
                            match last {
                                Some(rec) => r.append_memo(&R::manual_memo_line(&rec)),
                                None => r.append_memo(&format!("[{}] (아직 스핀 결과 없음)", util::hhmmss())),
                            }
                            util::blur_active();
                        }>"+ 현재 결과 넣기"</button>
                        <button class="btn sm" on:click=move |_| {
                            let line = st.with_untracked(|s| {
                                if s.counts.is_empty() { return None; }
                                let mut sorted = s.counts.clone();
                                sorted.sort_by(|a, b| b.1.cmp(&a.1));
                                let parts: Vec<String> = sorted.iter().map(|(n, c)| format!("{}:{}회", n, c)).collect();
                                Some(format!("[{}] 📊 누적 {}회 요약 ({}) → {}", util::hhmm(), s.history.len(), s.presets[s.sel].name, parts.join(", ")))
                            });
                            match line {
                                Some(l) => r.append_memo(&l),
                                None => util::alert("아직 돌린 통계가 없습니다."),
                            }
                            util::blur_active();
                        }>"+ 누적 통계"</button>
                        <button class="btn sm ok-text" on:click=move |_| {
                            if util::copy_text(&r.memo.get_untracked()) { r.memo_fb.set("✔ 클립보드에 복사됨!".into()); }
                            util::blur_active();
                        }>"전체 복사"</button>
                        <button class="btn sm danger-text" on:click=move |_| {
                            if !r.memo.get_untracked().trim().is_empty() && util::confirm("메모장 내용을 모두 비울까요?") {
                                r.memo.set(String::new());
                            }
                            util::blur_active();
                        }>"비우기"</button>
                    </div>
                    <textarea class="input memo grow" node_ref=memo_el prop:value=r.memo
                        on:input=move |e| { r.memo.set(event_target_value(&e)); r.memo_fb.set(String::new()); }>
                    </textarea>
                </div>
            </section>
        </div>
    }
}
