#!/usr/bin/env bash
# Runs a Test Lab game-loop test for every line of a parts file (one comma-separated list of scenario numbers per line),
# the parts in parallel, each in a results directory of its own: <results dir> for a single part, <results dir>-part<k>
# otherwise. Appends FIREBASE_RESULTS_DIR (the first) and FIREBASE_RESULTS_DIRS (all) to $GITHUB_ENV before launching.
# Exits with the first failing part's code; a part that outlasts the wait is a warning and keeps what completed.
# Usage: firebase-loops.sh <ios|android> <app> <results dir> <timeout minutes> <wait seconds> <parts file> <devices file>
set -uo pipefail
PLATFORM=$1
APP=$2
RESULTS_DIR=$3
TIMEOUT_MINUTES=$4
WAIT_SECONDS=$5
PARTS=()
while IFS= read -r LINE; do [ -n "$LINE" ] && PARTS+=("$LINE"); done < "$6"
DEVICES=()
while IFS= read -r LINE; do [ -n "$LINE" ] && DEVICES+=("$LINE"); done < "$7"
if [ "$PLATFORM" = ios ]; then
  RUN=(gcloud beta firebase test ios run)
  LIMIT=gtimeout
else
  RUN=(gcloud firebase test android run)
  LIMIT=timeout
fi

DIRS=()
for k in "${!PARTS[@]}"; do
  if [ "${#PARTS[@]}" -gt 1 ]; then DIRS+=("$RESULTS_DIR-part$k"); else DIRS+=("$RESULTS_DIR"); fi
done
echo "FIREBASE_RESULTS_DIR=${DIRS[0]}" >> "$GITHUB_ENV"
echo "FIREBASE_RESULTS_DIRS=${DIRS[*]}" >> "$GITHUB_ENV"

for k in "${!PARTS[@]}"; do
  echo "Part $k: scenarios ${PARTS[$k]} -> ${DIRS[$k]}"
  (
    "$LIMIT" --signal=INT --kill-after=30s "${WAIT_SECONDS}s" "${RUN[@]}" --type game-loop --app "$APP" "${DEVICES[@]}" \
      --scenario-numbers="${PARTS[$k]}" --timeout "${TIMEOUT_MINUTES}m" \
      --results-bucket="$FIREBASE_RESULTS_BUCKET" --results-dir="${DIRS[$k]}" > "firebase_output_$k.log" 2>&1
    echo $? > "firebase_exit_$k"
  ) &
done
wait

CODE=0
for k in "${!PARTS[@]}"; do
  echo "----- part $k -----"
  cat "firebase_output_$k.log"
  PART_CODE=$(cat "firebase_exit_$k")
  if [ "$PART_CODE" -eq 124 ]; then
    echo "::warning::Firebase part $k outlasted ${WAIT_SECONDS}s; collecting completed device results"
  elif [ "$PART_CODE" -ne 0 ]; then
    echo "Firebase exit code of part $k: $PART_CODE"
    [ "$CODE" -eq 0 ] && CODE=$PART_CODE
  fi
done
cat firebase_output_*.log > firebase_output.log
exit "$CODE"
