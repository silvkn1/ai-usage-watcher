#!/bin/bash
# AI Usage Watcher - pembungkus statusline Claude Code.
# 1) Menyimpan HANYA objek rate_limits (+ waktu tangkap) ke
#    %LOCALAPPDATA%\AIUsageWatcher\ratelimits.json (tulis atomik).
# 2) Meneruskan input yang sama, byte demi byte, ke perintah statusline lama
#    (disimpan di statusline-chain.txt oleh installer), lalu mencetak outputnya.
# Tidak ada data yang dikirim ke mana pun. Hanya builtin bash agar cepat.

IFS= read -r -d '' input

base="${LOCALAPPDATA:-$HOME/AppData/Local}"
base="${base//\\//}"                       # C:\Users\x -> C:/Users/x
if [ -d "$base/AIUsageWatcher" ]; then
  dir="$base/AIUsageWatcher"
elif [ -d "$base/ClaudeUsageWidget" ]; then
  dir="$base/ClaudeUsageWidget"
else
  dir="$base/AIUsageWatcher"
fi

# Objek rate_limits (berisi objek satu tingkat: five_hour, seven_day, dst).
flat="${input//[$'\r\n']/}"
re='"rate_limits":[[:space:]]*\{([[:space:]]*"[a-z_]+":[[:space:]]*\{[^{}]*\}[[:space:]]*,?)*[[:space:]]*\}'
if [[ $flat =~ $re ]]; then
  [ -d "$dir" ] || mkdir -p "$dir" 2>/dev/null
  printf -v now '%(%s)T' -1
  tmp="$dir/ratelimits.json.tmp"
  { printf '{"captured_at":%s,%s}' "$now" "${BASH_REMATCH[0]}" > "$tmp" && mv -f "$tmp" "$dir/ratelimits.json"; } 2>/dev/null
fi

chain="$dir/statusline-chain.txt"
if [ -s "$chain" ]; then
  cmd=$(<"$chain")
  printf '%s' "$input" | eval "$cmd"
fi
exit 0
