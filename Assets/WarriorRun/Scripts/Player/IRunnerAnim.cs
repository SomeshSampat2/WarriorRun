namespace WarriorRun.Player
{
    /// <summary>
    /// Animation callbacks PlayerController/GameManager drive. Implemented by the
    /// procedural RunnerAnimator and the rigged MannequinAnimator alike.
    /// </summary>
    public interface IRunnerAnim
    {
        void OnRunStart();
        void OnJump();
        void OnDoubleJump();
        void OnSlide(bool on);
        void OnLaneSwitch(int dir);
        void OnLand();
        void OnDeath();
    }
}
