// 탭 3: 팀 나누기 (팀 수 선택 · 시드 분산 · 1~5티어 균등 분배 · 팀장 뽑기)
use crate::util::{self, Sfx};
use crate::{tab_accepts_keys, use_modal};
use leptos::leptos_dom::helpers::IntervalHandle;
use leptos::*;
use std::time::Duration;

#[derive(Clone)]
struct Member {
    name: String,
    seed: bool,
    tier: u8, // 1(최상) ~ 5, 기본 3
}

#[derive(Clone)]
struct Assigned {
    name: String,
    seed: bool,
    tier: u8,
    leader: bool,
}

#[derive(Clone)]
struct HistEntry {
    round: u32,
    time: String,
    members: usize,
    teams: usize,
    text: String,
}

struct TState {
    team_count: usize,
    pick_leader: bool,
    tier_mode: bool,
    members: Vec<Member>,
    teams: Vec<Vec<Assigned>>,
    shuffling: bool,
    shuffle_ticks: u32,
    round: u32,
    history: Vec<HistEntry>, // 최신이 앞
}

impl TState {
    fn seed_count(&self) -> usize {
        self.members.iter().filter(|m| m.seed).count()
    }

    fn seed_exceeded(&self) -> bool {
        !self.tier_mode && self.seed_count() > self.team_count
    }

    fn assign(&mut self) {
        if self.tier_mode {
            self.assign_tier_balanced();
        } else {
            self.assign_seeded();
        }
        let leader = self.pick_leader;
        for list in self.teams.iter_mut() {
            util::shuffle(list);
            if leader && !list.is_empty() {
                let li = util::rand_int(list.len());
                list[li].leader = true;
                list.swap(0, li);
            }
        }
    }

    fn to_assigned(m: &Member) -> Assigned {
        Assigned { name: m.name.clone(), seed: m.seed, tier: m.tier, leader: false }
    }

    fn assign_seeded(&mut self) {
        let k = self.team_count;
        let mut seeds: Vec<&Member> = self.members.iter().filter(|m| m.seed).collect();
        let mut regs: Vec<&Member> = self.members.iter().filter(|m| !m.seed).collect();
        util::shuffle(&mut seeds);
        util::shuffle(&mut regs);
        // 팀 순서를 섞어 +1명 자리가 특정 팀에 몰리지 않게
        let mut order: Vec<usize> = (0..k).collect();
        util::shuffle(&mut order);
        let mut teams: Vec<Vec<Assigned>> = vec![Vec::new(); k];
        for (i, m) in seeds.into_iter().chain(regs).enumerate() {
            teams[order[i % k]].push(Self::to_assigned(m));
        }
        self.teams = teams;
    }

    fn assign_tier_balanced(&mut self) {
        let k = self.team_count;
        let n = self.members.len();
        // 1. 티어별로 묶고 묶음 안에서 섞기
        let mut pool: Vec<&Member> = Vec::with_capacity(n);
        for tier in 1..=5u8 {
            let mut g: Vec<&Member> = self.members.iter().filter(|m| m.tier.clamp(1, 5) == tier).collect();
            util::shuffle(&mut g);
            pool.extend(g);
        }
        // 2. 섞은 팀 순서로 스네이크 배치
        let mut order: Vec<usize> = (0..k).collect();
        util::shuffle(&mut order);
        let mut teams: Vec<Vec<Assigned>> = vec![Vec::new(); k];
        for (i, m) in pool.into_iter().enumerate() {
            let pass = i / k;
            let off = i % k;
            let oi = if pass % 2 == 0 { off } else { k - 1 - off };
            teams[order[oi]].push(Self::to_assigned(m));
        }
        // 3. 1:1 교환으로 팀 평균 티어 편차 최소화
        if n >= k && k >= 2 {
            let avg = self.members.iter().map(|m| m.tier as f64).sum::<f64>() / n as f64;
            for _ in 0..24 {
                let mut improved = false;
                let mut cur = balance_cost(&teams, avg);
                for t1 in 0..k {
                    for t2 in (t1 + 1)..k {
                        for a in 0..teams[t1].len() {
                            for b in 0..teams[t2].len() {
                                if teams[t1][a].tier == teams[t2][b].tier {
                                    continue;
                                }
                                swap_between(&mut teams, t1, a, t2, b);
                                let c = balance_cost(&teams, avg);
                                if c < cur - 1e-6 {
                                    cur = c;
                                    improved = true;
                                } else {
                                    swap_between(&mut teams, t1, a, t2, b);
                                }
                            }
                        }
                    }
                }
                if !improved {
                    break;
                }
            }
        }
        self.teams = teams;
    }

    fn summary(&self) -> String {
        let mut out = vec![if self.tier_mode {
            format!("[👥 팀 나누기 결과 - 총 {}명 / {}팀 · 등급 밸런스]", self.members.len(), self.team_count)
        } else {
            format!("[👥 팀 나누기 결과 - 총 {}명 / {}팀]", self.members.len(), self.team_count)
        }];
        for (t, list) in self.teams.iter().enumerate() {
            let names: Vec<String> = list
                .iter()
                .map(|m| {
                    let p = if m.leader { "👑" } else { "" };
                    if self.tier_mode {
                        format!("{}{}({}티어)", p, m.name, m.tier)
                    } else {
                        format!("{}{}", p, m.name)
                    }
                })
                .collect();
            if self.tier_mode && !list.is_empty() {
                out.push(format!("• {}팀 ({}명 · 평균 {:.1}티어): {}", t + 1, list.len(), avg_tier(list), names.join(", ")));
            } else {
                out.push(format!(
                    "• {}팀 ({}명): {}",
                    t + 1,
                    list.len(),
                    if names.is_empty() { "-".to_string() } else { names.join(", ") }
                ));
            }
        }
        out.join("\n")
    }

    fn quota_text(&self) -> String {
        let n = self.members.len();
        let k = self.team_count;
        if n == 0 {
            format!("현재 {}팀 선택됨 — 아래에 참가자를 추가해 주세요", k)
        } else if n < k {
            format!("총 {}명 / {}팀 (일부 팀은 빈 팀이 됩니다)", n, k)
        } else {
            let base = n / k;
            let rem = n % k;
            if rem == 0 {
                format!("총 {}명 → {}팀 (팀당 {}명씩 균등 배정)", n, k, base)
            } else {
                format!("총 {}명 → {}팀 ({}명 {}팀 · {}명 {}팀)", n, k, base + 1, rem, base, k - rem)
            }
        }
    }
}

fn swap_between(teams: &mut [Vec<Assigned>], t1: usize, a: usize, t2: usize, b: usize) {
    let (left, right) = teams.split_at_mut(t2);
    std::mem::swap(&mut left[t1][a], &mut right[0][b]);
}

fn balance_cost(teams: &[Vec<Assigned>], overall: f64) -> f64 {
    let mut cost = 0.0;
    let mut min_t1 = usize::MAX;
    let mut max_t1 = 0usize;
    for list in teams.iter().filter(|l| !l.is_empty()) {
        let avg = avg_tier(list);
        let d = avg - overall;
        cost += list.len() as f64 * d * d;
        let c1 = list.iter().filter(|m| m.tier == 1).count();
        min_t1 = min_t1.min(c1);
        max_t1 = max_t1.max(c1);
    }
    if max_t1 > min_t1 + 1 {
        cost += (max_t1 - min_t1 - 1) as f64 * 4.0;
    }
    cost
}

fn avg_tier(list: &[Assigned]) -> f64 {
    if list.is_empty() {
        return 0.0;
    }
    list.iter().map(|m| m.tier as f64).sum::<f64>() / list.len() as f64
}

fn tier_class(t: u8) -> &'static str {
    match t {
        1 => "tier t1",
        2 => "tier t2",
        3 => "tier t3",
        4 => "tier t4",
        _ => "tier t5",
    }
}

#[derive(Clone, Copy)]
struct T {
    st: RwSignal<TState>,
    summary: RwSignal<String>,
    copy_fb: RwSignal<String>,
    timer: StoredValue<Option<IntervalHandle>>,
}

impl T {
    fn stop_timer(self) {
        if let Some(h) = self.timer.get_value() {
            h.clear();
        }
        self.timer.set_value(None);
    }

    fn space(self) {
        util::blur_active();
        let (exceeded, n, shuffling) = self.st.with_untracked(|s| (s.seed_exceeded(), s.members.len(), s.shuffling));
        if exceeded {
            return;
        }
        if n < 2 {
            util::alert("팀을 나누려면 왼쪽에서 최소 2명 이상의 참가자를 추가해 주세요.");
            return;
        }
        if shuffling {
            self.finish();
            return;
        }
        self.st.update(|s| {
            s.shuffle_ticks = 6;
            s.shuffling = true;
            s.assign();
        });
        util::play(Sfx::Tick);
        let h = set_interval_with_handle(move || self.tick(), Duration::from_millis(55)).ok();
        self.timer.set_value(h);
    }

    fn tick(self) {
        let done = self.st.with_untracked(|s| s.shuffle_ticks <= 1);
        if done {
            self.finish();
            return;
        }
        self.st.update(|s| {
            s.shuffle_ticks -= 1;
            s.assign();
        });
        util::play(Sfx::Tick);
    }

    fn finish(self) {
        self.stop_timer();
        let mut text = String::new();
        self.st.update(|s| {
            s.shuffling = false;
            s.assign();
            s.round += 1;
            text = s.summary();
            s.history.insert(
                0,
                HistEntry { round: s.round, time: util::hhmmss(), members: s.members.len(), teams: s.team_count, text: text.clone() },
            );
        });
        util::play(Sfx::Win);
        self.summary.set(text);
        self.copy_fb.set(String::new());
    }

    /// 시드 초과로 버튼이 막히면 진행 중이던 섞기 애니메이션도 멈춘다
    fn after_members_changed(self) {
        if self.st.with_untracked(|s| s.seed_exceeded() && s.shuffling) {
            self.stop_timer();
            self.st.update(|s| s.shuffling = false);
        }
    }
}

#[component]
pub fn TeamTab() -> impl IntoView {
    let t = T {
        st: create_rw_signal(TState {
            team_count: 2,
            pick_leader: false,
            tier_mode: false,
            members: vec![],
            teams: vec![],
            shuffling: false,
            shuffle_ticks: 0,
            round: 0,
            history: vec![],
        }),
        summary: create_rw_signal(String::new()),
        copy_fb: create_rw_signal(String::new()),
        timer: store_value(None),
    };
    let st = t.st;
    let modal = use_modal();
    let input_text = create_rw_signal(String::new());

    window_event_listener(ev::keydown, move |e: web_sys::KeyboardEvent| {
        if !tab_accepts_keys(3) || e.is_composing() || util::focused_is_text_input() {
            return;
        }
        if e.code() == "Space" {
            e.prevent_default();
            if !e.repeat() {
                t.space();
            }
        }
    });

    let set_count = move |c: usize| {
        st.update(|s| s.team_count = c.clamp(2, 12));
        t.after_members_changed();
        util::blur_active();
    };

    let add_from_input = move || {
        let raw = input_text.get_untracked();
        let names = util::split_lines(&raw.replace('\t', ","), true);
        input_text.set(String::new());
        if !names.is_empty() {
            st.update(|s| {
                for n in names {
                    s.members.push(Member { name: n, seed: false, tier: 3 });
                }
            });
        }
    };

    let open_bulk = move |_| {
        let text = st.with_untracked(|s| s.members.iter().map(|m| m.name.clone()).collect::<Vec<_>>().join("\n"));
        modal.open(
            "참가자 명단 일괄 입력",
            "한 줄에 한 명씩(또는 쉼표로 구분하여) 참가자 이름을 붙여넣으세요:",
            text,
            move |res| {
                let names = util::split_lines(&res, true);
                st.update(|s| {
                    let mut prev = s.members.clone();
                    s.members = names
                        .into_iter()
                        .map(|n| match prev.iter().position(|p| p.name == n) {
                            Some(i) => {
                                let p = prev.remove(i);
                                Member { name: n, seed: p.seed, tier: p.tier }
                            }
                            None => Member { name: n, seed: false, tier: 3 },
                        })
                        .collect();
                });
                t.after_members_changed();
            },
        );
    };

    let fill_numbers = move |_| {
        let def = st.with_untracked(|s| (s.team_count * 2).max(6));
        util::blur_active();
        let Some(ans) = util::prompt("총 몇 명으로 채울까요? (2 ~ 60명):", &def.to_string()) else { return };
        if ans.trim().is_empty() {
            return;
        }
        match ans.trim().parse::<usize>() {
            Ok(c) if (2..=60).contains(&c) => st.update(|s| {
                s.members = (1..=c).map(|i| Member { name: format!("{}번 참가자", i), seed: false, tier: 3 }).collect();
            }),
            _ => util::alert("2에서 60 사이의 숫자를 입력해 주세요."),
        }
    };

    let tier_mode = move || st.with(|s| s.tier_mode);

    let member_rows = move || {
        st.with(|s| {
            if s.members.is_empty() {
                return view! { <div class="empty">"참가자 명단이 비어 있습니다."<br/>"위 입력창에서 추가하거나 [일괄 입력], [번호 생성]을 사용해 보세요."</div> }.into_view();
            }
            let seed_cnt = s.seed_count();
            let exceeded = seed_cnt > s.team_count;
            let at_cap = seed_cnt >= s.team_count;
            let k = s.team_count;
            s.members
                .iter()
                .enumerate()
                .map(|(i, m)| {
                    let border = if s.tier_mode {
                        match m.tier {
                            1 => "tier1",
                            2 => "tier2",
                            _ => "",
                        }
                    } else if m.seed {
                        if exceeded { "seed-over" } else { "seed" }
                    } else {
                        ""
                    };
                    let controls = if s.tier_mode {
                        let cur = m.tier;
                        (1..=5u8)
                            .map(|tv| {
                                let is_cur = cur == tv;
                                view! {
                                    <button class=if is_cur { format!("tier-btn cur {}", tier_class(tv)) } else { "tier-btn".to_string() }
                                        title=format!("{}티어로 설정", tv)
                                        on:click=move |_| { st.update(|s| s.members[i].tier = tv); util::blur_active(); }>
                                        {if is_cur { format!("{}티어", tv) } else { tv.to_string() }}
                                    </button>
                                }
                            })
                            .collect_view()
                    } else {
                        let seed = m.seed;
                        let disabled = !seed && at_cap;
                        let tip = if disabled {
                            format!("시드는 팀 수(현재 {}팀)까지만 지정할 수 있습니다.", k)
                        } else if seed {
                            "클릭하여 시드 해제".to_string()
                        } else {
                            "시드로 지정된 인원들은 같은 팀에 몰리지 않고 각 팀에 골고루 분산됩니다".to_string()
                        };
                        let cls = if seed { if exceeded { "btn xs seed-btn over" } else { "btn xs seed-btn on" } } else { "btn xs seed-btn" };
                        // 비활성 버튼에서도 툴팁이 보이도록 span으로 감싼다
                        view! {
                            <span title=tip.clone()>
                                <button class=cls disabled=disabled title=tip on:click=move |_| {
                                    st.update(|s| {
                                        if !s.members[i].seed && s.seed_count() >= s.team_count { return; }
                                        s.members[i].seed = !s.members[i].seed;
                                    });
                                    util::blur_active();
                                }>{if seed { "★시드" } else { "☆시드" }}</button>
                            </span>
                        }
                        .into_view()
                    };
                    view! {
                        <div class=format!("member-row {}", border)>
                            <span class="grow ellipsis">{format!("{}. {}", i + 1, m.name)}</span>
                            <span class="row gap2">{controls}</span>
                            <button class="icon-btn danger-text" title="삭제" on:click=move |_| {
                                st.update(|s| { s.members.remove(i); });
                                t.after_members_changed();
                                util::blur_active();
                            }>"✕"</button>
                        </div>
                    }
                    .into_view()
                })
                .collect_view()
        })
    };

    let seed_status = move || {
        st.with(|s| {
            let c = s.seed_count();
            if c > s.team_count {
                (format!("⚠️ 시드 초과: {} / {}명 (시드 해제 필요)", c, s.team_count), "danger-text bold")
            } else {
                (format!("★ 시드 현황: {} / {}명 (팀 수 이하)", c, s.team_count), if c > 0 { "warn-text" } else { "muted" })
            }
        })
    };

    let board = move || {
        st.with(|s| {
            if s.teams.is_empty() {
                return view! {
                    <div class="empty big">"왼쪽에서 팀 개수를 고르고 참가자를 추가한 뒤"<br/>"아래 [🎲 랜덤 팀 나누기! (Space)] 버튼을 눌러주세요."</div>
                }
                .into_view();
            }
            let cols = if s.teams.len() <= 4 { 2 } else { 3 };
            let tier = s.tier_mode;
            let preview = s.shuffling;
            view! {
                <div class="teams-grid" style=format!("grid-template-columns:repeat({},minmax(0,1fr))", cols)>
                    {s.teams.iter().enumerate().map(|(ti, list)| {
                        let col = util::color(ti);
                        let badge = if tier && !list.is_empty() {
                            format!("{}명 · 평균 {:.1}티어", list.len(), avg_tier(list))
                        } else {
                            format!("{}명", list.len())
                        };
                        view! {
                            <div class="team-card" style=format!("border-color:{}", if preview { "#4B5060" } else { col })>
                                <div class="row between mb8">
                                    <span class="row gap6"><span class="dot lg" style:background=col></span><b class="team-name">{format!("{}팀", ti + 1)}</b></span>
                                    <span class=if tier { "pill info" } else { "pill" }>{badge}</span>
                                </div>
                                {if list.is_empty() {
                                    view! { <span class="muted small">"(배정 인원 없음)"</span> }.into_view()
                                } else {
                                    view! {
                                        <div class="chips">
                                            {list.iter().map(|m| {
                                                let label = if m.leader { format!("👑 {}", m.name) } else if m.seed && !tier { format!("★ {}", m.name) } else { m.name.clone() };
                                                view! {
                                                    <span class="member-chip" class:leader=m.leader>
                                                        {label}
                                                        {tier.then(|| view! { <span class=tier_class(m.tier)>{format!("{}티어", m.tier)}</span> })}
                                                    </span>
                                                }
                                            }).collect_view()}
                                        </div>
                                    }.into_view()
                                }}
                            </div>
                        }
                    }).collect_view()}
                </div>
            }
            .into_view()
        })
    };

    let split_label = move || {
        st.with(|s| {
            if s.shuffling && !s.seed_exceeded() {
                "🎲 팀 섞는 중...".to_string()
            } else if s.round > 0 {
                "🎲 다시 랜덤 팀 나누기! (Space)".to_string()
            } else {
                "🎲 랜덤 팀 나누기! (Space)".to_string()
            }
        })
    };
    let split_tip = move || {
        st.with(|s| {
            if s.seed_exceeded() {
                format!("시드가 초과하였습니다. (현재 시드 {}명 / 최대 {}명)\n시드를 해제하거나 팀 개수를 늘려주세요.", s.seed_count(), s.team_count)
            } else {
                String::new()
            }
        })
    };

    view! {
        <div class="cols team">
            <section class="card col">
                <div class="subcard violet mb8">
                    <div class="row between mb6">
                        <b>"팀 개수 선택 (2~12팀)"</b>
                        <div class="row">
                            <button class="btn sq" on:click=move |_| set_count(st.with_untracked(|s| s.team_count) - 1)>"−"</button>
                            <b class="step-val violet-text">{move || format!("{} 팀", st.with(|s| s.team_count))}</b>
                            <button class="btn sq violet" on:click=move |_| set_count(st.with_untracked(|s| s.team_count) + 1)>"+"</button>
                        </div>
                    </div>
                    <div class="seg full mb6">
                        {(2..=6usize).map(|k| view! {
                            <button class="seg-btn violet" class:active=move || st.with(|s| s.team_count == k) on:click=move |_| set_count(k)>{format!("{}팀", k)}</button>
                        }).collect_view()}
                    </div>
                    <div class="muted small ellipsis">{move || st.with(|s| s.quota_text())}</div>
                </div>
                <label class="check mb6">
                    <input type="checkbox" prop:checked=move || st.with(|s| s.pick_leader)
                        on:change=move |e| { let v = event_target_checked(&e); st.update(|s| s.pick_leader = v); util::blur_active(); }/>
                    "👑 각 팀에서 팀장(대표) 1명 자동 뽑기"
                </label>
                <div class="subcard row between mb8" class:accent=tier_mode>
                    <span class="bold" class:accent-text=tier_mode>"⚖️ 등급(티어) 균등 분배"</span>
                    <button class="btn sm" class:accent=tier_mode on:click=move |_| {
                        st.update(|s| s.tier_mode = !s.tier_mode);
                        if !st.with_untracked(|s| s.teams.is_empty()) {
                            t.summary.set(st.with_untracked(|s| s.summary()));
                        }
                        t.after_members_changed();
                        util::blur_active();
                    }>{move || if tier_mode() { "ON (1~5티어)" } else { "OFF" }}</button>
                </div>
                <div class="card-head">
                    <div class="row gap6"><h2>"명단"</h2><span class="pill accent">{move || format!("{}명", st.with(|s| s.members.len()))}</span></div>
                    <div class="row gap4">
                        <button class="btn sm" on:click=open_bulk>"일괄 입력"</button>
                        <button class="btn sm" title="이름 입력 없이 인원 수만큼 '1번 ~ N번'으로 빠르게 채웁니다" on:click=fill_numbers>"번호 생성"</button>
                        <button class="btn sm danger-text" on:click=move |_| {
                            st.update(|s| { s.members.clear(); s.teams.clear(); });
                            t.after_members_changed();
                            util::blur_active();
                        }>"비우기"</button>
                    </div>
                </div>
                <div class="row gap6 mb6">
                    <input class="input grow" placeholder="이름 입력 후 Enter (쉼표 구분 가능)" prop:value=input_text
                        on:input=move |e| input_text.set(event_target_value(&e))
                        on:keydown=move |e| { if e.key() == "Enter" && !e.is_composing() { e.prevent_default(); add_from_input(); } }/>
                    <button class="btn primary" on:click=move |_| add_from_input()>"추가"</button>
                </div>
                <div class="row between mb6 small">
                    <Show when=move || !tier_mode() fallback=move || view! {
                        <span class="accent-text">"1티어(최상)~5티어(하) · 기본 3티어"</span>
                        <button class="btn xs" on:click=move |_| { st.update(|s| s.members.iter_mut().for_each(|m| m.tier = 3)); util::blur_active(); }>"전원 3티어"</button>
                    }>
                        <span class=move || seed_status().1>{move || seed_status().0}</span>
                        <Show when=move || st.with(|s| s.seed_count() > 0)>
                            <button class="btn xs warn-text" on:click=move |_| { st.update(|s| s.members.iter_mut().for_each(|m| m.seed = false)); util::blur_active(); }>"시드 초기화"</button>
                        </Show>
                    </Show>
                </div>
                <div class="list grow">{member_rows}</div>
            </section>

            <section class="card col">
                <div class="card-head">
                    <h2>"👥 팀 배정 결과 보드"</h2>
                    <span class="small" class:accent-text=tier_mode class:muted=move || !tier_mode()>
                        {move || if tier_mode() { "⚖️ 등급 밸런스 ON — 1~5티어 실력이 각 팀에 균등 분배됩니다" } else { "★시드 표시된 인원은 서로 다른 팀으로 균등 분산됩니다" }}
                    </span>
                </div>
                <div class="board-bg grow scroll">{board}</div>
                <span class="mt10 block" title=split_tip>
                    <button class="btn violet lg full" disabled=move || st.with(|s| s.seed_exceeded()) title=split_tip
                        on:click=move |_| t.space()>{split_label}</button>
                </span>
            </section>

            <section class="card col">
                <div class="card-head"><h2>"공유용 텍스트 & 배정 기록"</h2></div>
                <textarea class="input memo grow2" prop:value=t.summary on:input=move |e| t.summary.set(event_target_value(&e))></textarea>
                <div class="ok small right mt6">{move || t.copy_fb.get()}</div>
                <button class="btn lg ok-text mt4" on:click=move |_| {
                    if util::copy_text(&t.summary.get_untracked()) { t.copy_fb.set("✔ 클립보드에 복사되었습니다!".into()); }
                    util::blur_active();
                }>"📋 카톡 / 디스코드용 복사"</button>
                <div class="muted small mt10 mb6">"최근 팀 배정 기록 (클릭 시 복원)"</div>
                <div class="list grow">
                    {move || st.with(|s| s.history.iter().map(|h| {
                        let h2 = h.clone();
                        view! {
                            <div class="hist-row" on:click=move |_| {
                                t.summary.set(h2.text.clone());
                                t.copy_fb.set(format!("#{}회차 결과를 불러왔습니다", h2.round));
                            }>{format!("#{}회차 ({}) — {}명 / {}팀 배정", h.round, h.time, h.members, h.teams)}</div>
                        }
                    }).collect_view())}
                </div>
            </section>
        </div>
    }
}
