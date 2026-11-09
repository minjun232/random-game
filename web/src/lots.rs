// 탭 2: 제비뽑기 (당첨·꽝 / 순서 뽑기 / 직접 편집)
use crate::util::{self, Sfx};
use crate::{tab_accepts_keys, use_modal};
use leptos::*;

#[derive(Clone)]
struct Ticket {
    slot: usize,
    text: String,
    special: bool,
    order_number: bool,
    revealed: bool,
    pick_order: usize, // 0 = '모두 공개'로 열림
    drawer: Option<String>,
}

struct LState {
    mode: usize, // 0 당첨·꽝, 1 순서, 2 직접 편집
    rank_suffix: bool,
    total: usize,
    special: usize,
    special_label: String,
    normal_label: String,
    custom: Vec<(String, bool)>,
    drawers: Vec<String>,
    tickets: Vec<Ticket>,
    picked_seq: usize,
    show_hint: bool,
}

impl LState {
    fn reset_and_shuffle(&mut self) {
        let mut t: Vec<Ticket> = Vec::new();
        let mk = |text: String, special: bool, order: bool| Ticket {
            slot: 0,
            text,
            special,
            order_number: order,
            revealed: false,
            pick_order: 0,
            drawer: None,
        };
        if self.mode == 1 {
            let unit = if self.rank_suffix { "등" } else { "번" };
            for i in 1..=self.total {
                t.push(mk(format!("{}{}", i, unit), i == 1, true));
            }
        } else if self.mode == 2 && self.custom.len() >= 2 {
            for (text, sp) in &self.custom {
                t.push(mk(text.clone(), *sp, false));
            }
        } else {
            let sp = if self.special_label.trim().is_empty() { "🎉 당첨!".to_string() } else { self.special_label.trim().to_string() };
            let nm = if self.normal_label.trim().is_empty() { "꽝".to_string() } else { self.normal_label.trim().to_string() };
            for i in 0..self.total {
                let is_sp = i < self.special;
                t.push(mk(if is_sp { sp.clone() } else { nm.clone() }, is_sp, false));
            }
        }
        util::shuffle(&mut t);
        for (i, x) in t.iter_mut().enumerate() {
            x.slot = i + 1;
        }
        self.tickets = t;
        self.picked_seq = 0;
    }

    fn picked(&self) -> Vec<Ticket> {
        let mut p: Vec<Ticket> = self.tickets.iter().filter(|t| t.revealed && t.pick_order > 0).cloned().collect();
        p.sort_by_key(|t| t.pick_order);
        p
    }
}

#[derive(Clone, Copy)]
struct L {
    st: RwSignal<LState>,
    copy_fb: RwSignal<String>,
}

impl L {
    fn reshuffle(self) {
        self.st.update(|s| s.reset_and_shuffle());
        self.copy_fb.set(String::new());
    }

    fn pick(self, idx: usize) {
        let mut sfx = None;
        self.st.update(|s| {
            if idx >= s.tickets.len() || s.tickets[idx].revealed {
                return;
            }
            s.picked_seq += 1;
            let seq = s.picked_seq;
            let drawer = s.drawers.get(seq - 1).cloned();
            let t = &mut s.tickets[idx];
            t.revealed = true;
            t.pick_order = seq;
            t.drawer = drawer;
            sfx = Some(if t.special {
                Sfx::Win
            } else if t.order_number {
                Sfx::Reach
            } else {
                Sfx::Tick
            });
        });
        if let Some(s) = sfx {
            util::play(s);
        }
    }

    fn space(self) {
        util::blur_active();
        let unpicked: Vec<usize> = self.st.with_untracked(|s| {
            s.tickets.iter().enumerate().filter(|(_, t)| !t.revealed).map(|(i, _)| i).collect()
        });
        if unpicked.is_empty() {
            self.reshuffle();
            return;
        }
        self.pick(unpicked[util::rand_int(unpicked.len())]);
    }

    fn reveal_all(self) {
        util::blur_active();
        let mut any = false;
        self.st.update(|s| {
            for t in s.tickets.iter_mut().filter(|t| !t.revealed) {
                t.revealed = true;
                t.pick_order = 0;
                any = true;
            }
        });
        if any {
            util::play(Sfx::Reach);
        }
    }

    fn copy(self) {
        let text = self.st.with_untracked(|s| {
            let mut out = vec!["[제비뽑기 결과]".to_string()];
            let picked = s.picked();
            if picked.is_empty() {
                for t in &s.tickets {
                    out.push(format!("#{}번 제비: {}", t.slot, if t.revealed { t.text.as_str() } else { "(미공개)" }));
                }
            } else {
                for t in &picked {
                    let who = match &t.drawer {
                        Some(d) => format!("{}번째 {}", t.pick_order, d),
                        None => format!("{}번째 (#{}번)", t.pick_order, t.slot),
                    };
                    out.push(format!("{} → {}", who, t.text));
                }
            }
            out.join("\n")
        });
        if util::copy_text(&text) {
            self.copy_fb.set("✔ 클립보드에 복사되었습니다!".into());
        }
        util::blur_active();
    }
}

#[component]
pub fn LotsTab() -> impl IntoView {
    let mut init = LState {
        mode: 0,
        rank_suffix: false,
        total: 6,
        special: 1,
        special_label: "🎉 당첨!".into(),
        normal_label: "꽝".into(),
        custom: vec![
            ("🥇 1등 당첨".into(), true),
            ("🥈 2등 당첨".into(), true),
            ("꽝".into(), false),
            ("꽝".into(), false),
            ("꽝".into(), false),
        ],
        drawers: vec![],
        tickets: vec![],
        picked_seq: 0,
        show_hint: true,
    };
    init.reset_and_shuffle();
    let l = L { st: create_rw_signal(init), copy_fb: create_rw_signal(String::new()) };
    let st = l.st;
    let modal = use_modal();

    window_event_listener(ev::keydown, move |e: web_sys::KeyboardEvent| {
        if !tab_accepts_keys(2) || e.is_composing() || util::focused_is_text_input() {
            return;
        }
        if e.code() == "Space" {
            e.prevent_default();
            if !e.repeat() {
                l.space();
            }
        }
    });

    let mode = move || st.with(|s| s.mode);
    let set_mode = move |m: usize| {
        st.update(|s| s.mode = m);
        l.reshuffle();
        util::blur_active();
    };
    let change_total = move |d: i32| {
        st.update(|s| {
            let next = s.total as i32 + d;
            if !(2..=36).contains(&next) {
                return;
            }
            s.total = next as usize;
            if s.special >= s.total {
                s.special = s.total - 1;
            }
        });
        l.reshuffle();
        util::blur_active();
    };
    let change_special = move |d: i32| {
        st.update(|s| {
            let next = s.special as i32 + d;
            if next < 1 || next as usize >= s.total {
                return;
            }
            s.special = next as usize;
        });
        l.reshuffle();
        util::blur_active();
    };

    let open_custom = move |_| {
        let text = st.with_untracked(|s| s.custom.iter().map(|(t, _)| t.clone()).collect::<Vec<_>>().join("\n"));
        modal.open(
            "제비 문구 직접 편집",
            "한 줄에 제비 하나씩 문구를 입력하세요 (2~36개).\n'꽝', '통과' 외의 문구는 자동으로 당첨(강조) 제비로 표시됩니다:",
            text + "\n",
            move |res| {
                let list: Vec<(String, bool)> = util::split_lines(&res, false)
                    .into_iter()
                    .take(36)
                    .map(|t| {
                        let sp = util::is_special_result_text(&t);
                        (t, sp)
                    })
                    .collect();
                if list.len() < 2 {
                    util::alert("제비는 최소 2개 이상 입력해 주세요.");
                    return;
                }
                st.update(|s| {
                    s.custom = list;
                    s.mode = 2;
                });
                l.reshuffle();
            },
        );
    };

    let open_drawers = move |_| {
        let text = st.with_untracked(|s| s.drawers.join("\n"));
        modal.open(
            "뽑는 사람 명단 설정 (선택 사항)",
            "제비를 뽑을 순서대로 이름을 한 줄에 한 명씩 입력하세요.\n비워두면 이름 없이 1번째, 2번째 순서로만 깔끔하게 기록됩니다:",
            text,
            move |res| st.update(|s| s.drawers = util::split_lines(&res, false)),
        );
    };

    let chips = [("🎉 당첨!", "꽝"), ("💣 벌칙!", "통과"), ("☕ 커피 쏘기", "잘 마실게요"), ("👑 술래", "생존")];

    let tickets_view = move || {
        st.with(|s| {
            let small = s.tickets.len() > 20;
            s.tickets
                .iter()
                .enumerate()
                .map(|(i, t)| {
                    if !t.revealed {
                        view! {
                            <div class="ticket closed" class:small=small on:click=move |_| { util::blur_active(); l.pick(i); }>
                                <span class="t-cap">{format!("#{}번 제비", t.slot)}</span>
                                <span class="t-icon">"🎫"</span>
                                <span class="t-hint">"클릭해 뽑기"</span>
                            </div>
                        }
                        .into_view()
                    } else {
                        let tone = if t.special { "special" } else if t.order_number { "order" } else { "plain" };
                        let cap = if t.pick_order > 0 {
                            match &t.drawer {
                                Some(d) => d.clone(),
                                None => format!("#{} · {}번째", t.slot, t.pick_order),
                            }
                        } else {
                            format!("#{} · 전체공개", t.slot)
                        };
                        view! {
                            <div class=format!("ticket open {}", tone) class:small=small>
                                <span class="t-cap">{cap}</span>
                                <span class="t-main">{t.text.clone()}</span>
                            </div>
                        }
                        .into_view()
                    }
                })
                .collect_view()
        })
    };

    let status = move || {
        st.with(|s| {
            let rem = s.tickets.iter().filter(|t| !t.revealed).count();
            if rem == 0 {
                "모든 제비가 공개되었습니다! [다시 섞기] 또는 Space로 새 판을 시작하세요.".to_string()
            } else {
                let next = s.drawers.get(s.picked_seq).map(|n| format!(" · 다음 차례: [{}]", n)).unwrap_or_default();
                format!("남은 제비: {} / {}개{}", rem, s.tickets.len(), next)
            }
        })
    };

    let badge = move || {
        st.with(|s| {
            if s.mode == 1 {
                let unit = if s.rank_suffix { "등" } else { "번" };
                return Some((format!("🔢 순서 뽑기 (1{}~{}{})", unit, s.tickets.len(), unit), "info"));
            }
            if !s.show_hint {
                return None;
            }
            let rem_sp = s.tickets.iter().filter(|t| !t.revealed && t.special).count();
            Some((format!("남은 주요 제비: {}개", rem_sp), if rem_sp > 0 { "warn" } else { "" }))
        })
    };

    let all_done = move || st.with(|s| s.tickets.iter().all(|t| t.revealed));

    let log_view = move || {
        st.with(|s| {
            let picked = s.picked();
            if picked.is_empty() {
                return view! { <div class="empty">"아직 뽑은 제비가 없습니다."<br/>"가운데 카드를 클릭하거나 Space를 눌러 뽑아보세요!"</div> }.into_view();
            }
            picked
                .into_iter()
                .map(|t| {
                    let tone = if t.special { "win" } else if t.order_number { "info" } else { "" };
                    let who = match &t.drawer {
                        Some(d) => format!("{}. {} (#{}번)", t.pick_order, d, t.slot),
                        None => format!("{}번째 (#{}번 제비)", t.pick_order, t.slot),
                    };
                    view! {
                        <div class=format!("log-row {}", tone)>
                            <span class="grow ellipsis">{who}</span>
                            <span class=format!("pill solid {}", tone)>{t.text.clone()}</span>
                        </div>
                    }
                })
                .collect_view()
        })
    };

    view! {
        <div class="cols lots">
            <section class="card col">
                <div class="card-head"><h2>"제비 구성 설정"</h2></div>
                <div class="scroll grow">
                    <div class="section-title">"뽑기 방식 선택"</div>
                    <div class="seg full">
                        {["🎯 당첨·꽝", "🔢 순서 뽑기", "✏️ 직접 편집"].into_iter().enumerate().map(|(i, n)| view! {
                            <button class="seg-btn" class:active=move || mode() == i on:click=move |_| set_mode(i)>{n}</button>
                        }).collect_view()}
                    </div>
                    <div class="hr"></div>
                    <div class="section-title">"세부 제비 설정"</div>
                    <Show when=move || mode() != 2>
                        <div class="stepper">
                            <span>{move || if mode() == 1 { "순서 제비 수 (2~36명)" } else { "전체 제비 수 (2~36개)" }}</span>
                            <div class="row">
                                <button class="btn sq" on:click=move |_| change_total(-1)>"−"</button>
                                <b class="step-val">{move || format!("{}개", st.with(|s| s.total))}</b>
                                <button class="btn sq primary" on:click=move |_| change_total(1)>"+"</button>
                            </div>
                        </div>
                    </Show>
                    <Show when=move || mode() == 0>
                        <div class="stepper">
                            <span>"당첨 / 벌칙 개수"</span>
                            <div class="row">
                                <button class="btn sq" on:click=move |_| change_special(-1)>"−"</button>
                                <b class="step-val">{move || format!("{}개", st.with(|s| s.special))}</b>
                                <button class="btn sq primary" on:click=move |_| change_special(1)>"+"</button>
                            </div>
                        </div>
                        <label class="field-label">"주요 제비 문구 (당첨 / 벌칙 등)"</label>
                        <input class="input" prop:value=move || st.with(|s| s.special_label.clone())
                            on:input=move |e| { let v = event_target_value(&e); st.update(|s| s.special_label = v); }/>
                        <div class="row wrap gap4 mt6 mb8">
                            {chips.into_iter().map(|(sp, nm)| view! {
                                <button class="btn sm warn-text" on:click=move |_| {
                                    st.update(|s| { s.special_label = sp.into(); s.normal_label = nm.into(); });
                                    l.reshuffle();
                                    util::blur_active();
                                }>{sp}</button>
                            }).collect_view()}
                        </div>
                        <label class="field-label">"기본(나머지) 제비 문구"</label>
                        <input class="input" prop:value=move || st.with(|s| s.normal_label.clone())
                            on:input=move |e| { let v = event_target_value(&e); st.update(|s| s.normal_label = v); }/>
                    </Show>
                    <Show when=move || mode() == 1>
                        <div class="subcard accent">
                            <div class="field-label">"표기 형식 선택"</div>
                            <div class="seg full mb8">
                                <button class="seg-btn" class:active=move || !st.with(|s| s.rank_suffix)
                                    on:click=move |_| { st.update(|s| s.rank_suffix = false); l.reshuffle(); util::blur_active(); }>"1번 ~ N번 (순서)"</button>
                                <button class="seg-btn" class:active=move || st.with(|s| s.rank_suffix)
                                    on:click=move |_| { st.update(|s| s.rank_suffix = true); l.reshuffle(); util::blur_active(); }>"1등 ~ N등 (순위)"</button>
                            </div>
                            <div class="small">{move || st.with(|s| {
                                let u = if s.rank_suffix { "등" } else { "번" };
                                format!("총 {0}장의 제비에 [1{1} ~ {0}{1}] 번호가 한 장씩 무작위로 섞여 배치됩니다.", s.total, u)
                            })}</div>
                        </div>
                    </Show>
                    <Show when=move || mode() == 2>
                        <div class="subcard violet">
                            <div class="small pre mb8">{move || st.with(|s| {
                                let preview: Vec<String> = s.custom.iter().take(6).map(|(t, _)| t.clone()).collect();
                                let extra = if s.custom.len() > 6 { format!(" 외 {}장", s.custom.len() - 6) } else { String::new() };
                                format!("현재 총 {}장 구성됨:\n{}{}", s.custom.len(), preview.join(", "), extra)
                            })}</div>
                            <button class="btn violet full" on:click=open_custom>"📝 제비 문구 목록 편집하기"</button>
                        </div>
                    </Show>
                    <div class="hr"></div>
                    <div class="section-title">"(선택) 뽑는 사람 기록"</div>
                    <div class="subcard row between">
                        <span class="muted small ellipsis">{move || st.with(|s| if s.drawers.is_empty() { "미지정 (1번째, 2번째 순서로 기록)".to_string() } else { format!("명단 지정됨: 총 {}명", s.drawers.len()) })}</span>
                        <button class="btn sm" on:click=open_drawers>"명단 설정"</button>
                    </div>
                </div>
                <button class="btn warn lg mt10" on:click=move |_| { l.reshuffle(); util::blur_active(); }>"🎲 설정대로 제비 새로 섞기"</button>
            </section>

            <section class="card col">
                <div class="card-head">
                    <h2>"🎫 제비뽑기 보드"</h2>
                    <Show when=move || mode() != 1>
                        <button class="btn sm" on:click=move |_| { st.update(|s| s.show_hint = !s.show_hint); util::blur_active(); }>
                            {move || if st.with(|s| s.show_hint) { "남은 당첨 수 표시: ON" } else { "남은 당첨 수 표시: OFF" }}
                        </button>
                    </Show>
                </div>
                <div class="status-bar row between">
                    <span class="ellipsis">{status}</span>
                    {move || badge().map(|(t, tone)| view! { <span class=format!("pill {}", tone)>{t}</span> })}
                </div>
                <div class="board-bg grow scroll"><div class="tickets">{tickets_view}</div></div>
                <div class="action-grid three mt10">
                    <button class="btn lg" class:warn=move || !all_done() class:success=all_done on:click=move |_| l.space()>
                        {move || if all_done() { "🔄 새로 섞어서 다시 시작 (Space)" } else { "👆 랜덤 1개 뽑기 (Space)" }}
                    </button>
                    <button class="btn lg" on:click=move |_| l.reveal_all()>"👀 남은 제비 모두 공개"</button>
                    <button class="btn lg primary" on:click=move |_| { l.reshuffle(); util::blur_active(); }>"🔄 다시 섞기"</button>
                </div>
            </section>

            <section class="card col">
                <div class="card-head"><h2>"제비 뽑기 기록"</h2></div>
                <div class="list grow">{log_view}</div>
                <div class="ok small right mt8">{move || l.copy_fb.get()}</div>
                <button class="btn lg ok-text mt4" on:click=move |_| l.copy()>"📋 제비뽑기 결과 복사"</button>
            </section>
        </div>
    }
}
