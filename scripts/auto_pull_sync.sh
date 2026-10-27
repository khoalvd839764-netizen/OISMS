#!/bin/zsh
# ========================================================
# Script: Tự động kéo code OISMS an toàn hàng ngày
# ========================================================

REPO_DIR="/Users/bo/OISMS"
LOG_FILE="$REPO_DIR/scripts/auto_sync.log"

echo "=== [$(date '+%Y-%m-%d %H:%M:%S')] Bắt đầu đồng bộ mã nguồn OISMS ===" >> "$LOG_FILE"

cd "$REPO_DIR" || exit 1

# 1. Tạm lưu code đang làm dở (nếu có) để tránh mất mát
STASH_OUTPUT=$(git stash 2>&1)
echo "1. Cất an toàn code đang dở: $STASH_OUTPUT" >> "$LOG_FILE"

# 2. Về nhánh develop và kéo code mới nhất
git checkout develop >> "$LOG_FILE" 2>&1
PULL_OUTPUT=$(git pull origin develop 2>&1)
echo "2. Kéo develop: $PULL_OUTPUT" >> "$LOG_FILE"

# 3. Về nhánh làm việc OISMS-42 và merge develop vào
git checkout feature/OISMS-42-sprint-3-backend-2 >> "$LOG_FILE" 2>&1
MERGE_OUTPUT=$(git merge develop 2>&1)
echo "3. Gộp vào nhánh cá nhân: $MERGE_OUTPUT" >> "$LOG_FILE"

# 4. Phục hồi lại code dở dang nếu trước đó có stash
if [[ "$STASH_OUTPUT" != *"No local changes to save"* ]]; then
    git stash pop >> "$LOG_FILE" 2>&1
    echo "4. Phục hồi lại code dở dang đã cất." >> "$LOG_FILE"
fi

echo "=== Hoàn tất đồng bộ lúc $(date '+%H:%M:%S') ===" >> "$LOG_FILE"
echo "" >> "$LOG_FILE"
