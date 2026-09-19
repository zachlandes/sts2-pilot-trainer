#!/usr/bin/env bash
# Stands in for the retail Slay the Spire 2 executable at the points that matter to
# the two helpers that launch it, so their whole lifecycle is held without the game:
#
#   opened without --force-steam=off   prints Steamworks' "No appID found" and stops,
#                                      exactly as the client does on the popup
#   with it                            reports the skip the real log carries, counts
#                                      itself as a running client, and runs until TERM,
#                                      taking FAKE_GAME_TERM_SECONDS to finish - the
#                                      retail client's teardown is anywhere from
#                                      seconds to minutes
#   with --headless and FAKE_SOAK_EXIT_WITHOUT_DONE  quits after FAKE_SOAK_DONE_AFTER
#                                      seconds having written nothing: a night that
#                                      ended without finishing
#   with --headless and FAKE_SOAK_DONE  the retail soak's client: after
#                                      FAKE_SOAK_DONE_AFTER seconds writes the file
#                                      FAKE_SOAK_DONE names the way RetailSoak writes
#                                      soak-done, with FAKE_SOAK_OUTCOME as the one
#                                      run's outcome (default "ended"), and exits the
#                                      way NGame.Quit does. Without --headless it
#                                      never writes it: a soak script that dropped the
#                                      flag would wait out its deadline, which is the
#                                      point.
#
# Every launch appends what it saw - cwd, its entries, args, SteamAppId - to
# FAKE_GAME_LOG, and its pid to FAKE_GAME_PIDS while it counts as running.
# RetailClientLaunchTests and RetailSoakScriptTests run it; demo/RETAIL-SOAK.md runs
# the same file.
{
  printf 'cwd\t%s\n' "$PWD"
  printf 'entries\t%s\n' "$(ls -A | tr '\n' ' ')"
  printf 'args\t%s\n' "$*"
  printf 'SteamAppId\t%s\n' "${SteamAppId-unset}"
} >> "$FAKE_GAME_LOG"
case " $* " in
  *' --force-steam=off '*) ;;
  *) echo '[ERROR] Steamworks initialization failed! Result: k_ESteamAPIInitResult_FailedGeneric, message: No appID found.  Either launch the game from Steam, or put the file steam_appid.txt containing the correct appID in your game folder.'; exit 1 ;;
esac
echo '[INFO] Steam initialization skipped (editor mode). Use --force-steam to enable.'
echo "$$" >> "$FAKE_GAME_PIDS"
on_term() {
  if [[ "${FAKE_GAME_TERM_SECONDS:-0}" -gt 0 ]]; then sleep "$FAKE_GAME_TERM_SECONDS"; fi
  echo "exited on TERM" >> "$FAKE_GAME_LOG"
  exit 0
}
trap on_term TERM
headless=0
case " $* " in *' --headless '*) headless=1 ;; esac
if [[ "$headless" == 1 && -n "${FAKE_SOAK_EXIT_WITHOUT_DONE:-}" ]]; then
  # A client that went away without finishing the night - a crash, a quit from
  # somewhere else - which the script must not read an earlier night's file for
  sleep "${FAKE_SOAK_DONE_AFTER:-2}"
  echo "quit without writing soak-done" >> "$FAKE_GAME_LOG"
  exit 0
fi
if [[ "$headless" == 1 && -n "${FAKE_SOAK_DONE:-}" ]]; then
  sleep "${FAKE_SOAK_DONE_AFTER:-2}"
  mkdir -p "$(dirname "$FAKE_SOAK_DONE")"
  cat > "$FAKE_SOAK_DONE" <<DONE
{
  "schema": "sts2-pilot-trainer/retail-soak-done/v1",
  "finished_at_utc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "runs_planned": 1,
  "runs_started": 1,
  "runs": [
    { "run": 1, "seed": "STANDIN", "outcome": "${FAKE_SOAK_OUTCOME:-ended}" }
  ],
  "recorder_version": "stand-in"
}
DONE
  echo "soak-done written; quitting the way NGame.Quit does" >> "$FAKE_GAME_LOG"
  exit 0
fi
while :; do sleep 1; done
