using UnityEngine;

namespace SuminTools.RhythmChart
{
    /// <summary>
    /// 한 난이도의 생성 규칙을 담은 에셋. Inspector 의 슬라이더로 조정한다.
    /// 패키지에 들어 있는 기본 프리셋은 읽기 전용일 수 있으므로, 고치려면 프로젝트로 복사해서 차트에 지정한다.
    /// </summary>
    public class DifficultyPreset : ScriptableObject
    {
        [SerializeField] Difficulty difficulty;
        [SerializeField] DifficultySettings settings = new DifficultySettings();

        /// <summary>이 프리셋이 기본으로 대응하는 난이도(표시용).</summary>
        public Difficulty Difficulty => difficulty;

        /// <summary>생성 규칙.</summary>
        public DifficultySettings Settings => settings;

        /// <summary>난이도와 규칙을 한꺼번에 지정한다.</summary>
        public void Set(Difficulty difficulty, DifficultySettings settings)
        {
            this.difficulty = difficulty;
            this.settings = settings.Clone();
        }
    }
}
