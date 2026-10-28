# Chronology

Desktop timekeeping app.

# Config

`config.json` lives in the application's working directory. Null properties are treated as unset and defaulted.

<!-- configdocgen:start -->

| Key                           | Type                                                                                     | Default     | Notes                                                                                                                                    |
| ----------------------------- | ---------------------------------------------------------------------------------------- | ----------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `version`                     | int                                                                                      | `1`         | Config schema version, do not edit.                                                                                                      |
| `reminder.enabled`            | bool                                                                                     | `false`     | Plays audio alert when application has been idle for the specified time.                                                                 |
| `reminder.count`              | int                                                                                      | `1`         | Must be > 0.                                                                                                                             |
| `reminder.intervalMinutes`    | double                                                                                   | `30`        | Must be > 0.                                                                                                                             |
| `reminder.soundFile`          | string                                                                                   | `null`      | `.wav` file; `null` = system sound. File must exist.                                                                                     |
| `weeklySum.enabled`           | bool                                                                                     | `false`     | Show the weekly counter under Total.                                                                                                     |
| `weeklySum.goalHighlightShow` | bool                                                                                     | `true`      | Highlight the weekly sum when the goal is met.                                                                                           |
| `weeklySum.goalHours`         | double                                                                                   | `40`        | Must be > 0.                                                                                                                             |
| `weeklySum.goalShow`          | bool                                                                                     | `true`      | Show the `/ goal` suffix.                                                                                                                |
| `weeklySum.weekStart`         | `Sunday` \| `Monday` \| `Tuesday` \| `Wednesday` \| `Thursday` \| `Friday` \| `Saturday` | `Sunday`    |                                                                                                                                          |
| `theme.variant`               | `Default` \| `Light` \| `Dark`                                                           | `Default`   | Base theme variant - `Default` follows the OS.                                                                                           |
| `theme.overrideFile`          | string                                                                                   | `null`      | JSON theme override file; `null` = none. Relative paths resolve against the working directory. See `theme/midnight.json` for the format. |
| `store.type`                  | `Legacy` \| `Plaintext`                                                                  | `Plaintext` |                                                                                                                                          |

## `Legacy` store

| Key                    | Type                   | Default     | Notes                                                 |
| ---------------------- | ---------------------- | ----------- | ----------------------------------------------------- |
| `store.dataDirectory`  | string                 | `./timelog` | Relative paths resolve against the working directory. |
| `store.durationFormat` | `Hhmm` \| `Fractional` | `Hhmm`      | Duration format in files - `9:30` vs `9.50`.          |

## `Plaintext` store

| Key                   | Type   | Default     | Notes                                                 |
| --------------------- | ------ | ----------- | ----------------------------------------------------- |
| `store.dataDirectory` | string | `./timelog` | Relative paths resolve against the working directory. |

<!-- configdocgen:end -->
