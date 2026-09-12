#!/usr/bin/env bash
#
# One unattended pass of the crawl pipeline: discover, scrape, promote, report.
#
# Written to be run from cron, which means it assumes nothing about the
# environment it starts in — cron gives a process almost no PATH and a working
# directory of $HOME, and a script that works when pasted into a shell will
# quietly do nothing there.
#
# Install (as the user that owns the deployment, not root):
#
#   crontab -e
#   0 4 * * 0  /usr/bin/flock -n /tmp/rtp-crawl.lock /home/ubuntu/RecipeFinder/scripts/crawl.sh >> /home/ubuntu/crawl.log 2>&1
#
# flock makes a slow run harmless: if last week's crawl is somehow still going,
# this week's exits immediately rather than running a second crawler at the same
# site. Weekly is deliberate — see the note on diminishing returns at the bottom.

set -euo pipefail

# cron's PATH is typically just /usr/bin:/bin, which does happen to contain
# docker — but it costs nothing to be explicit rather than depend on it.
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export PATH

# The repository root, resolved from this script's own location, so the cron
# entry does not have to agree with a path hard-coded here.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

SITE="${SITE:-https://www.delish.com}"
LIMIT="${LIMIT:-200}"

COMPOSE=(docker compose -f docker-compose.prod.yml)

echo "=== $(date -Is)  crawl pass starting: $SITE (limit $LIMIT) ==="

# Discover appends to its output file, so a run that did not start from a clean
# file would grow it without bound. Deleting first keeps the list to one pass;
# nothing is lost, because the *staging table* is what remembers which URLs have
# already been fetched, and scrape skips those on its own.
rm -f crawl/urls.txt
mkdir -p crawl

# --user matters. The image runs as a non-root account that does not own this
# directory on the host, so without it the write fails with a permission error
# that reads like a bug in the crawler. Running as the invoking user instead
# means the host directory needs no loosened permissions at all.
"${COMPOSE[@]}" run --rm \
	--user "$(id -u):$(id -g)" \
	-v "$ROOT/crawl:/out" worker \
	discover "$SITE" \
	--match=/cooking/recipe-ideas/ --match=-recipe/ \
	--limit "$LIMIT" --out /out/urls.txt

# Fetches only what is not already staged. Re-running is cheap and safe; it is
# also slow on purpose, honouring robots.txt and any Crawl-delay the site asks
# for, so most of the wall time here is deliberate waiting.
"${COMPOSE[@]}" run --rm worker scrape /crawl/urls.txt

# Turns staged pages into recipes. No network calls, so this part is fast.
"${COMPOSE[@]}" run --rm worker promote

"${COMPOSE[@]}" run --rm worker status

echo "=== $(date -Is)  crawl pass finished ==="

# A note on how much this actually yields over time.
#
# `discover` reads the site's sitemaps in the order the site publishes them and
# stops at --limit. Whether a later run therefore sees *new* recipes depends on
# those sitemaps being ordered newest-first, which is a convention rather than a
# rule and has not been verified for this site. If successive runs report no new
# pages staged, that is the reason — raise LIMIT to reach further in:
#
#   LIMIT=500 ./scripts/crawl.sh
#
# Raising it costs someone else's bandwidth, so raise it deliberately rather
# than on a schedule.
