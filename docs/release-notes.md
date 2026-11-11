Local DPS Tracker 1.4.1 preserves finished boss results.

- Delayed damage after a confirmed boss defeat can no longer create a small second fight and replace the finished result. Final hits within the existing 250 ms window still count. Positive HP allows the same boss entity to start a new fight.
- Closing an auxiliary game connection preserves encounter history when the existing capture filter still covers the game.

Windows self-tests cover delayed damage, multiple bosses, and a confirmed new life. A live boss kill on this build and the separate new-party tracking issue still need verification.
