// 랜덤 게임 v2.0 — 웹(WASM) 버전.
// 데스크톱 v1.x(WPF)의 4개 탭을 그대로 옮겼고, 모든 데이터는 메모리에만 존재한다.
mod ladder;
mod lots;
mod roulette;
mod team;
mod util;

use leptos::*;
use std::rc::Rc;

/// 여러 줄 입력 대화상자 요청 (원본 ShowMultilineDialog 대체)
#[derive(Clone)]
pub struct ModalReq {
    pub title: String,
    pub info: String,
    pub text: String,
    pub on_ok: Rc<dyn Fn(String)>,
}

#[derive(Clone, Copy)]
pub struct Modal(pub RwSignal<Option<ModalReq>>);

impl Modal {
    pub fn open(&self, title: &str, info: &str, text: String, on_ok: impl Fn(String) + 'static) {
        util::blur_active();
        self.0.set(Some(ModalReq {
            title: title.to_string(),
            info: info.to_string(),
            text,
            on_ok: Rc::new(on_ok),
        }));
    }
    pub fn is_open(&self) -> bool {
        self.0.with_untracked(|m| m.is_some())
    }
}

pub fn use_modal() -> Modal {
    use_context::<Modal>().expect("Modal context")
}

/// 현재 활성 탭 (0 룰렛, 1 사다리, 2 제비뽑기, 3 팀 나누기)
#[derive(Clone, Copy)]
pub struct ActiveTab(pub RwSignal<usize>);

/// 각 탭이 전역 키 입력을 받을지 판단: 해당 탭이 활성이고, 대화상자가 닫혀 있을 때
pub fn tab_accepts_keys(tab: usize) -> bool {
    let active = use_context::<ActiveTab>().expect("ActiveTab");
    let modal = use_modal();
    active.0.get_untracked() == tab && !modal.is_open()
}

#[component]
fn ModalView() -> impl IntoView {
    let modal = use_modal();
    let area = create_node_ref::<html::Textarea>();

    let close = move || modal.0.set(None);
    let apply = move || {
        let text = area.get_untracked().map(|a| a.value()).unwrap_or_default();
        let req = modal.0.get_untracked();
        modal.0.set(None);
        if let Some(r) = req {
            (r.on_ok)(text);
        }
    };

    create_effect(move |_| {
        if modal.0.with(|m| m.is_some()) {
            request_animation_frame(move || {
                if let Some(a) = area.get_untracked() {
                    let _ = a.focus();
                    let len = a.value().len() as u32;
                    let _ = a.set_selection_range(len, len);
                }
            });
        }
    });

    view! {
        <Show when=move || modal.0.with(|m| m.is_some())>
            <div class="modal-backdrop" on:mousedown=move |e| {
                if e.target() == e.current_target() { close(); }
            }>
                <div class="modal" on:keydown=move |e| {
                    if e.key() == "Escape" { close(); }
                }>
                    <div class="modal-title">{move || modal.0.with(|m| m.as_ref().map(|r| r.title.clone()).unwrap_or_default())}</div>
                    <div class="modal-info">{move || modal.0.with(|m| m.as_ref().map(|r| r.info.clone()).unwrap_or_default())}</div>
                    <textarea class="input modal-text" node_ref=area
                        prop:value=move || modal.0.with_untracked(|m| m.as_ref().map(|r| r.text.clone()).unwrap_or_default())>
                    </textarea>
                    <div class="modal-actions">
                        <button class="btn ghost" on:click=move |_| close()>"취소"</button>
                        <button class="btn primary" on:click=move |_| apply()>"적용하기"</button>
                    </div>
                </div>
            </div>
        </Show>
    }
}

#[component]
fn App() -> impl IntoView {
    let active = create_rw_signal(0usize);
    provide_context(ActiveTab(active));
    provide_context(Modal(create_rw_signal(None)));

    let tabs = ["🎡 연속 룰렛", "┣┫ 사다리 게임", "🎫 제비뽑기", "👥 팀 나누기"];
    let hints = [
        "Space 돌리기 · A 자동 · R 카운트 리셋 · Esc 입력창 나가기",
        "참가자 이름 클릭: 개별 출발 · Space 다음 출발 · Esc 입력창 나가기",
        "제비 카드 클릭: 직접 뽑기 · Space 랜덤 1개 뽑기 · Esc 입력창 나가기",
        "팀 개수를 고르고 명단을 넣은 뒤 Space 또는 [팀 나누기]",
    ];

    // 입력창에서 Esc → 입력창 밖으로 (단축키 복귀)
    window_event_listener(ev::keydown, move |e| {
        if e.key() == "Escape" && util::focused_is_text_input() {
            util::blur_active();
        }
    });

    let show = move |i: usize| move || if active.get() == i { "" } else { "none" };

    view! {
        <div class="shell">
            <header class="topbar">
                <div class="brand">"랜덤 게임"</div>
                <nav class="tabs">
                    {tabs.iter().enumerate().map(|(i, name)| view! {
                        <button class="tab" class:active=move || active.get() == i
                            on:click=move |_| { active.set(i); util::blur_active(); }>
                            {*name}
                        </button>
                    }).collect_view()}
                </nav>
                <div class="hint">{move || hints[active.get()]}</div>
            </header>
            <main class="tab-body">
                <div class="tab-pane" style:display=show(0)><roulette::RouletteTab/></div>
                <div class="tab-pane" style:display=show(1)><ladder::LadderTab/></div>
                <div class="tab-pane" style:display=show(2)><lots::LotsTab/></div>
                <div class="tab-pane" style:display=show(3)><team::TeamTab/></div>
            </main>
            <ModalView/>
        </div>
    }
}

fn main() {
    console_error_panic_hook::set_once();
    mount_to_body(App);
}
