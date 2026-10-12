#!/usr/bin/env bash
# Generates the radio chatter into ../Sounds/Voice*.wav with the Piper text-to-speech engine,
# then gives them the grainy, band-limited sound of an early-80s speech chip (tools/crunch_voice.py).
#
# Needs a Piper binary and the en_GB-vctk-medium voice (VCTK corpus, CC BY 4.0). Override the paths with
# PIPER=/path/to/piper and VOICE=/path/to/model.onnx. Speaker 75 in that model is VCTK speaker p345.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
out="$here/../Sounds"
piper="${PIPER:-$HOME/.local/share/piper/bin/piper}"
voice="${VOICE:-$HOME/.local/share/piper/voices/en_GB-vctk-medium.onnx}"
speaker="${SPEAKER:-75}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

say() {
  local name="$1" text="$2"
  echo "$text" | "$piper" --model "$voice" --speaker "$speaker" --length_scale 0.95 \
    --noise_scale 0.4 --noise_w 0.5 --output_file "$tmp/$name.wav" >/dev/null 2>&1
  python3 "$here/crunch_voice.py" "$tmp/$name.wav" "$out/Voice$name.wav"
}

mkdir -p "$out"
say Welcome    "All wings, begin your attack run."
say Fighters   "Enemy fighters, coming in!"
say Surface    "Going in. Watch those gun emplacements."
say Towers     "Look out for the towers. Take out their guns."
say Trench     "We're in the trench. Stay on target."
say Vader      "Heavy fighter dead ahead! Watch yourself!"
say VaderGone  "He's spinning out of control!"
say Port       "Exhaust port coming up. Get ready to fire."
say Missed     "Torpedo missed. Come around for another run."
say GreatShot  "Great shot! Now get clear!"
say Force      "You trusted your instincts. Great shot!"
say ShieldsLow "Shields are failing!"
say GameOver   "Mission failed."
echo "Wrote voices to $(cd "$out" && pwd)"
