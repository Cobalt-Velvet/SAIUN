using System;
using UnityEngine;

namespace _SAIUN.Scripts.Core
{
    //포모도로 상태 정의

    public enum PomodoroState
    {
        Idle,          // 포모도로 미실행. 시계 위젯 모드.
        Focus,         // 집중 타이머 진행 중.
        ShortBreak,    // 단기 휴식.
        LongBreak,     // 장기 휴식 (전체 세트 완료 후).
        Interrupted,   // 방해 앱 감지 → 유예 중.
        Failed         // 유예 초과 → 작물 사망.
    }

    //포모도로 상태 머신

    public class PomodoroStateMachine : MonoBehaviour
    {
        // 현재 상태 (외부에서 읽기 전용)
        public PomodoroState CurrentState { get; private set; } = PomodoroState.Idle;

        // 상태 변경 이벤트 (구독자에게 알림)
        public event Action<PomodoroState, PomodoroState> OnStateChanged;

        //포모도로 상태 전환

        public void ChangeState(PomodoroState newState)
        {
            // 같은 상태로 전환 시도는 무시
            if (CurrentState == newState) return;

            // 잘못된 전환은 차단
            if (!IsValidTransition(CurrentState, newState))
            {
                Debug.LogWarning($"Invalid transition: {CurrentState} → {newState}");
                return;
            }

            PomodoroState previousState = CurrentState;
            CurrentState = newState;

            Debug.Log($"State changed: {previousState} → {newState}");
            OnStateChanged?.Invoke(previousState, newState);
        }

        //전환 유효성 검사

        private bool IsValidTransition(PomodoroState from, PomodoroState to)
        {
            switch (from)
            {
                case PomodoroState.Idle:
                    // IDLE에서는 FOCUS로만 진입 가능
                    return to == PomodoroState.Focus;

                case PomodoroState.Focus:
                    // FOCUS에서는 휴식/방해/IDLE(취소)로 전환 가능
                    return to == PomodoroState.ShortBreak
                        || to == PomodoroState.LongBreak
                        || to == PomodoroState.Interrupted
                        || to == PomodoroState.Idle;

                case PomodoroState.ShortBreak:
                    // 단기 휴식에서는 FOCUS로 복귀 또는 IDLE(취소)
                    return to == PomodoroState.Focus
                        || to == PomodoroState.Idle;

                case PomodoroState.LongBreak:
                    // 장기 휴식 후에는 IDLE로 복귀
                    return to == PomodoroState.Idle;

                case PomodoroState.Interrupted:
                    // 방해 유예 중에는 FOCUS 복귀 또는 FAILED
                    return to == PomodoroState.Focus
                        || to == PomodoroState.Failed;

                case PomodoroState.Failed:
                    // 실패 후에는 IDLE로 복귀
                    return to == PomodoroState.Idle;

                default:
                    return false;
            }
        }
    }
}