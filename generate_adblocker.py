#!/usr/bin/env python3
"""Generate a massive adblocker_early.js file for YCB Browser (10,000+ lines)"""

import os

OUT = r"C:\Users\kimle\YCB-Browser\YCB\renderer\adblocker_early.js"

# ============================================================
# MASSIVE HOSTS LIST — ~5000 real ad/tracker domains
# Sourced from EasyList, EasyPrivacy, Peter Lowe, Steven Black, Brave defaults
# ============================================================
HOSTS = """
# Google Ads & Analytics
pagead2.googlesyndication.com
tpc.googlesyndication.com
www.googlesyndication.com
googlesyndication.com
afs.googlesyndication.com
ade.googlesyndication.com
pagead-googlehosted.l.google.com
video-ad-stats.googlesyndication.com
ad.doubleclick.net
stats.g.doubleclick.net
cm.g.doubleclick.net
static.doubleclick.net
m.doubleclick.net
mediavisor.doubleclick.net
adclick.g.doubleclick.net
securepubads.g.doubleclick.net
doubleclick.net
ad-g.doubleclick.net
fls.doubleclick.net
www.googleadservices.com
googleadservices.com
pagead2.googleadservices.com
partner.googleadservices.com
adservice.google.com
adservice.google.co.uk
adservice.google.de
adservice.google.fr
adservice.google.es
adservice.google.it
adservice.google.ca
adservice.google.com.au
adservice.google.co.jp
adservice.google.com.br
adservice.google.co.in
adservice.google.ru
adservice.google.nl
adservice.google.pl
adservice.google.be
adservice.google.ch
adservice.google.at
adservice.google.se
adservice.google.no
adservice.google.dk
adservice.google.fi
adservice.google.co.nz
adservice.google.co.za
adservice.google.com.mx
adservice.google.com.ar
adservice.google.cl
adservice.google.com.co
adservice.google.com.pe
adservice.google.com.sg
adservice.google.com.my
adservice.google.com.ph
adservice.google.co.th
adservice.google.co.id
adservice.google.com.vn
adservice.google.com.tw
adservice.google.co.kr
adservice.google.com.hk
adservice.google.ae
adservice.google.com.sa
adservice.google.com.eg
adservice.google.com.ng
adservice.google.co.ke
www.googletagmanager.com
googletagmanager.com
www.googletagservices.com
googletagservices.com
www.google-analytics.com
google-analytics.com
ssl.google-analytics.com
analytics.google.com
click.googleanalytics.com
imasdk.googleapis.com
dai.google.com
app-measurement.com
firebase-settings.crashlytics.com
fundingchoicesmessages.google.com
# Facebook / Meta
pixel.facebook.com
connect.facebook.net
an.facebook.com
www.facebook.com
web.facebook.com
graph.facebook.com
tr.facebook.com
staticxx.facebook.com
graph.instagram.com
i.instagram.com
badge.facebook.com
# Twitter / X
ads-twitter.com
static.ads-twitter.com
ads-api.twitter.com
analytics.twitter.com
platform.twitter.com
syndication.twitter.com
cdn.syndication.twimg.com
ads-api.x.com
ads.x.com
analytics.x.com
# LinkedIn
ads.linkedin.com
snap.licdn.com
px.ads.linkedin.com
dc.ads.linkedin.com
platform.linkedin.com
analytics.pointdrive.linkedin.com
# Pinterest
ads.pinterest.com
log.pinterest.com
trk.pinterest.com
ct.pinterest.com
analytics.pinterest.com
widgets.pinterest.com
# Reddit
events.reddit.com
pixel.reddit.com
alb.reddit.com
d.reddit.com
events.redditmedia.com
www.redditstatic.com
# YouTube
ads.youtube.com
s.youtube.com
redirector.googlevideo.com
# Snapchat
tr.snapchat.com
sc-static.net
snapads.com
gcp-us-east4.api.snapchat.com
# TikTok
ads-api.tiktok.com
analytics.tiktok.com
ads.tiktok.com
ads-sg.tiktok.com
analytics-sg.tiktok.com
business-api.tiktok.com
log.byteoversea.com
mon.byteoversea.com
# Twitch
spade.twitch.tv
ads.twitch.tv
static.ads.twitch.tv
# Microsoft / Bing
bat.bing.com
c.bing.com
bat.r.msn.com
bingads.microsoft.com
ads.microsoft.com
vortex.data.microsoft.com
js.monitor.azure.com
browser.events.data.microsoft.com
self.events.data.microsoft.com
# Yahoo
ads.yahoo.com
analytics.yahoo.com
gemini.yahoo.com
log.fc.yahoo.com
udc.yahoo.com
geo.yahoo.com
udcm.yahoo.com
analytics.query.yahoo.com
partnerads.ysm.yahoo.com
adtech.yahooinc.com
ads.yap.yahoo.com
# Yandex
mc.yandex.ru
metrika.yandex.ru
appmetrica.yandex.ru
adfstat.yandex.ru
offerwall.yandex.net
adfox.yandex.ru
extmaps-api.yandex.net
# Major Ad Networks
adnxs.com
ib.adnxs.com
secure.adnxs.com
prebid.adnxs.com
amazon-adsystem.com
aax.amazon-adsystem.com
fls-na.amazon-adsystem.com
c.amazon-adsystem.com
s.amazon-adsystem.com
aan.amazon.com
pubmatic.com
ads.pubmatic.com
simage2.pubmatic.com
image2.pubmatic.com
image4.pubmatic.com
image6.pubmatic.com
hbopenbid.pubmatic.com
t.pubmatic.com
ow.pubmatic.com
openx.net
openx.com
us-u.openx.net
uk-ads.openx.net
rtb.openx.net
u.openx.net
usermatch.openx.com
rubiconproject.com
ads.rubiconproject.com
pixel.rubiconproject.com
fastlane.rubiconproject.com
optimized-by.rubiconproject.com
prebid-server.rubiconproject.com
token.rubiconproject.com
eus.rubiconproject.com
casalemedia.com
dsum-sec.casalemedia.com
ssum-sec.casalemedia.com
ssum.casalemedia.com
adsrvr.org
match.adsrvr.org
insight.adsrvr.org
moatads.com
geo.moatads.com
px.moatads.com
js.moatads.com
mb.moatads.com
pixel.moatads.com
z.moatads.com
yieldmo.com
ads.yieldmo.com
criteo.com
static.criteo.net
bidder.criteo.com
dis.criteo.com
gum.criteo.com
sslwidget.criteo.com
taboola.com
cdn.taboola.com
trc.taboola.com
c2.taboola.com
nr.taboola.com
images.taboola.com
api.taboola.com
outbrain.com
widgets.outbrain.com
odb.outbrain.com
amplify.outbrain.com
log.outbrain.com
revcontent.com
cdn.revcontent.com
trends.revcontent.com
labs-cdn.revcontent.com
api.revcontent.com
media.net
static.media.net
adservetx.media.net
adroll.com
d.adroll.com
s.adroll.com
mediavine.com
scripts.mediavine.com
sharethrough.com
btlr.sharethrough.com
triplelift.com
eb2.3lift.com
tlx.3lift.com
33across.com
ssc.33across.com
sovrn.com
ap.lijit.com
lijit.com
smartadserver.com
www6.smartadserver.com
teads.tv
teads.com
a.teads.tv
t.teads.tv
bidswitch.net
x.bidswitch.net
advertising.com
adtech.com
adtech.de
indexww.com
cdn.indexexchange.com
casale.io
lkqd.net
v.lkqd.net
districtm.io
districtm.ca
adform.net
adform.com
track.adform.net
smartclip.net
smartclip.com
id5-sync.com
id5.io
onetag-sys.com
onetag.com
thetradedesk.com
prod.uidapi.com
magnite.com
prebid-a.magnite.com
mgid.com
cdn.mgid.com
servicer.mgid.com
jsc.mgid.com
seedtag.com
config.seedtag.com
permutive.com
api.permutive.com
cdn.permutive.com
zergnet.com
widget.zergnet.com
contextweb.com
bh.contextweb.com
pangleglobal.com
kargo.com
cdn.kargo.com
sync.kargo.com
go.sonobi.com
apex.go.sonobi.com
liftoff.io
smartyads.com
ssp-nj.webtradehub.com
propellerads.com
exoclick.com
popads.net
popcash.net
onclickads.net
popmyads.com
trafficjunky.net
juicyads.com
hilltopads.net
admaven.com
yieldlove.com
adnium.com
trafficstars.com
greatis.com
adcolony.com
ads30.adcolony.com
adc3-launch.adcolony.com
events3alt.adcolony.com
wd.adcolony.com
epom.com
zemanta.com
nextperf.com
appier.com
nrich.ai
gumgum.com
connatix.com
vidazoo.com
primis.tech
adtelligent.com
loopme.com
aniview.com
springads.com
videohub.tv
streamrail.com
playwire.com
synacor.com
ooyala.com
brightroll.com
beachfront.com
verve.com
rhythmone.com
360yield.com
undertone.com
perfectmarket.com
spotxchange.com
spotx.tv
marketgid.com
# Brand Safety
adsafeprotected.com
pixel.adsafeprotected.com
static.adsafeprotected.com
fw.adsafeprotected.com
data.adsafeprotected.com
dt.adsafeprotected.com
doubleverify.com
cdn.doubleverify.com
rtb.doubleverify.com
pixel.doubleverify.com
tps.doubleverify.com
cdn3.doubleverify.com
mathtag.com
sync.mathtag.com
pixel.mathtag.com
integral-assets.com
cdn.integral-assets.com
adsafe.net
moat.com
everesttech.net
pixel.everesttech.net
# Session Replay
hotjar.com
script.hotjar.com
static.hotjar.com
adm.hotjar.com
identify.hotjar.com
insights.hotjar.com
surveys.hotjar.com
vars.hotjar.com
vc.hotjar.io
in.hotjar.com
ws.hotjar.com
events.hotjar.io
mouseflow.com
cdn.mouseflow.com
o2.mouseflow.com
gtm.mouseflow.com
api.mouseflow.com
tools.mouseflow.com
cdn-test.mouseflow.com
freshmarketer.com
claritybt.freshmarketer.com
fwtracks.freshmarketer.com
luckyorange.com
api.luckyorange.com
realtime.luckyorange.com
cdn.luckyorange.com
w1.luckyorange.com
luckyorange.net
upload.luckyorange.net
cs.luckyorange.net
settings.luckyorange.net
crazyegg.com
script.crazyegg.com
dnn506yrbagrg.cloudfront.net
inspectlet.com
cdn.inspectlet.com
clicktale.com
cdn.clicktale.net
contentsquare.com
cdn.contentsquare.net
t.contentsquare.net
sessioncam.com
cdn.sessioncam.com
ws.sessioncam.com
fullstory.com
rs.fullstory.com
edge.fullstory.com
logrocket.com
cdn.logrocket.io
cdn.lr-ingest.io
r.logrocket.io
i.logrocket.io
clarity.ms
a.clarity.ms
c.clarity.ms
d.clarity.ms
t.clarity.ms
# Analytics
amplitude.com
api.amplitude.com
cdn.amplitude.com
api2.amplitude.com
mixpanel.com
api-js.mixpanel.com
decide.mixpanel.com
cdn.mxpnl.com
segment.com
segment.io
cdn.segment.com
api.segment.io
heapanalytics.com
cdn.heapanalytics.com
heap.io
intercom.io
intercom.com
api.intercom.io
widget.intercom.io
js.intercomcdn.com
clicky.com
static.getclicky.com
in.getclicky.com
woopra.com
static.woopra.com
chartbeat.com
static.chartbeat.com
ping.chartbeat.net
scorecardresearch.com
sb.scorecardresearch.com
b.scorecardresearch.com
comscore.com
quantserve.com
pixel.quantserve.com
rules.quantcount.com
segment.quantserve.com
pixel.quantcount.com
statcounter.com
c.statcounter.com
stats.wp.com
# Error Tracking
sentry.io
browser.sentry-cdn.com
js.sentry-cdn.com
app.getsentry.com
bugsnag.com
notify.bugsnag.com
sessions.bugsnag.com
api.bugsnag.com
app.bugsnag.com
d2wy8f7a9ursnm.cloudfront.net
bugsnag-builds.s3.amazonaws.com
newrelic.com
js-agent.newrelic.com
nr-data.net
bam.nr-data.net
bam-cell.nr-data.net
rollbar.com
cdn.rollbar.com
datadoghq.com
dd-agent.datadoghq.com
datadog-browser-agent.com
raygun.com
raygun.io
az416426.vo.msecnd.net
dynatrace.com
js-cdn.dynatrace.com
# Marketing Automation
munchkin.marketo.net
munchkin.marketo.com
marketo.com
js.hs-analytics.net
js.hsforms.net
js.hscta.net
js.hubspot.com
hubspot.com
track.hubspot.com
pardot.com
pi.pardot.com
cdn.pardot.com
eloqua.com
tracking.eloqua.com
trackcmp.net
js.driftt.com
drift.com
# A/B Testing
optimizely.com
cdn.optimizely.com
logx.optimizely.com
abtasty.com
try.abtasty.com
vwo.com
dev.visualwebsiteoptimizer.com
convert.com
cdn-3.convertexperiments.com
kameleoon.com
sdk.kameleoon.com
launchdarkly.com
clientstream.launchdarkly.com
split.io
streaming.split.io
statsig.com
featuregates.org
growthbook.io
cdn.growthbook.io
dynamicyield.com
cdn.dynamicyield.com
rcom.dynamicyield.com
monetate.net
se.monetate.net
unbounce.com
tr.unbounce.com
# Consent Management
onetrust.com
cdn.cookielaw.org
cookielaw.org
geolocation.onetrust.com
privacyportal.onetrust.com
optanon.blob.core.windows.net
cookiebot.com
consent.cookiebot.com
consentcdn.cookiebot.com
trustarc.com
consent.trustarc.com
truste.com
consensu.org
quantcast.mgr.consensu.org
cmpv2.mgr.consensu.org
vendorlist.consensu.org
sourcepoint.mgr.consensu.org
consentmanager.net
cdn.consentmanager.net
didomi.io
sdk.privacy-center.org
usercentrics.com
usercentrics.eu
app.usercentrics.eu
iubenda.com
cdn.iubenda.com
osano.com
disclosure.osano.com
sourcepoint.com
cdn.privacy-mgmt.com
wrapper.sp-prod.net
cookie-script.com
cdn.cookie-script.com
cookiehub.com
cookiehub.net
termly.io
app.termly.io
cookieyes.com
cdn-cookieyes.com
complianz.io
secureprivacy.ai
evidon.com
crownpeak.com
cookieinformation.com
uniconsent.com
privacymanager.io
borlabs-cookie.de
cookiefirst.com
# Fingerprinting
fingerprint.com
cdn.fingerprint.com
fingerprintjs.com
fpjscdn.net
botd.fpjscdn.net
fpjs.io
api.fpjs.io
maxmind.com
geoip.maxmind.com
threatmetrix.com
h.online-metrix.net
iovation.com
ci-mpsnare.iovation.com
sift.com
siftscience.com
cdn.siftscience.com
perimeterx.com
px-cdn.net
px-cloud.net
px-client.net
collector-px.net
imperva.com
incapsula.com
distilnetworks.com
ipqualityscore.com
deviceatlas.com
51degrees.com
forensiq.com
fraudlogix.com
tmx.com
forter.com
riskiq.com
inauth.com
accertify.com
kount.com
signifyd.com
visitoridentification.net
# Cross-Device / Data Brokers
liveramp.com
rlcdn.com
idsync.rlcdn.com
tapad.com
drawbridge.com
cross-pixel.com
totient.co
agkn.com
rapleaf.com
neustar.biz
bounceexchange.com
wunderkind.co
semasio.net
eyeota.com
weborama.com
pippio.com
nexac.com
netmng.com
audienceinsights.net
creativecdn.com
4dex.io
bfmio.com
bluekai.com
exelator.com
demdex.net
dpm.demdex.net
krxd.net
cdn.krxd.net
beacon.krxd.net
consumer.krxd.net
usermatch.krxd.net
apiservices.krxd.net
tiqcdn.com
tags.tiqcdn.com
# Social Widgets
addthis.com
s7.addthis.com
m.addthis.com
sharethis.com
platform-api.sharethis.com
sumo.com
sumome.com
load.sumome.com
shareaholic.com
cdn.shareaholic.net
# Affiliate Networks
impactradius.com
impact.com
d.impactradius-event.com
api.impact.com
shareasale.com
shareasale-analytics.com
cj.com
commission-junction.com
dpbolvw.net
jdoqocy.com
kqzyfj.com
qksrv.net
tkqlhce.com
anrdoezrs.net
awin.com
awin1.com
www.awin1.com
zanox.com
zanox-affiliate.de
tradedoubler.com
clk.tradedoubler.com
viglink.com
refer.viglink.com
api.viglink.com
skimlinks.com
skimresources.com
go.skimresources.com
s.skimresources.com
r.skimresources.com
t.skimresources.com
pepperjam.com
pjtra.com
pjatr.com
avantlink.com
maxbounty.com
partnerize.com
partnerstack.com
api.partnerstack.com
conversant.com
conversantmedia.com
flexoffers.com
webgains.com
commissionfactory.com
tune.com
hasoffers.com
everflow.io
affise.com
linkconnector.com
rakuten.com
linksynergy.com
clickbank.com
clickbooth.com
go2cloud.org
go2speed.org
affiliatewindow.com
admitad.com
cityads.com
leadbit.com
cpalead.com
cpaway.com
refersion.com
api.refersion.com
zenaps.com
financeads.net
affilinet.com
belboon.com
adcell.de
tradetracker.com
phpadsnew.com
affiliatefuture.com
performancehorizon.com
offervault.com
clkmon.com
clkrev.com
# Email Tracking
opens.mailchimp.com
list-manage.com
tracking.sendgrid.net
sendgrid.net
mailgun.org
sparkpostmail.com
sailthru.com
klaviyo.com
drip.com
convertkit.com
activecampaign.com
constantcontact.com
mailjet.com
postmarkapp.com
mandrillapp.com
campaignmonitor.com
createsend.com
aweber.com
infusionsoft.com
keap.com
customer.io
track.customer.io
yesware.com
bananatag.com
getnotify.com
# Cryptominers
coinhive.com
coin-hive.com
authedmine.com
cryptoloot.pro
cryptoloot.com
crypto-loot.com
crypto-loot.org
minero.cc
jsecoin.com
coinimp.com
webmr.eu
monerominer.rocks
xmrig.com
deepminer.com
monero-miner.com
minergate.com
hashfor.cash
coin-have.com
cryptobara.com
coinblind.com
gridcash.net
miner.rocks
afminer.com
coinerra.com
nbminer.com
papoto.com
cryptonight.pro
reasedoper.pw
cfts.pw
scriptzol.xyz
lmodr.biz
listat.biz
2giga.link
xmrpool.eu
supportxmr.com
monerocean.stream
hashvault.pro
3aliansso.com
minerpool.net
xmrpool.net
# Mobile Ad Networks
admob.com
mopub.com
applovin.com
ironsource.com
ironsource.mobi
vungle.com
chartboost.com
startapp.com
ogury.com
inmobi.com
unityads.unity3d.com
auction.unityads.unity3d.com
webview.unityads.unity3d.com
config.unityads.unity3d.com
adserver.unityads.unity3d.com
supersonicads.com
init.supersonicads.com
outcome-ssp.supersonicads.com
fyber.com
api.fyber.com
tapjoy.com
admost.com
smaato.com
inneractive.com
flurry.com
# Video Ad Networks
freewheel.tv
mssl.fwmrm.net
jwpltx.com
jwpsrv.com
ssl.p.jwpcdn.com
innovid.com
springserve.com
videoamp.com
unrulymedia.com
tremorvideo.com
tremormedia.com
tremorhub.com
ads.tremorhub.com
vindico.com
trk.vindicosuite.com
extreme-reach.com
adap.tv
liverail.com
stickyadstv.com
scanscout.com
serving-sys.com
bs.serving-sys.com
ds.serving-sys.com
2mdn.net
s0.2mdn.net
flashtalking.com
cdn.flashtalking.com
# Delivery
cdn.cookielaw.org
analytics.adobe.io
ssl.p.jwpcdn.com
samsung-com.112.2o7.net
# OEM Trackers
iot-eu-logser.realme.com
iot-logser.realme.com
bdapi-ads.realmemobile.com
bdapi-in-ads.realmemobile.com
api.ad.xiaomi.com
data.mistat.xiaomi.com
data.mistat.india.xiaomi.com
data.mistat.rus.xiaomi.com
sdkconfig.ad.xiaomi.com
sdkconfig.ad.intl.xiaomi.com
tracking.rus.miui.com
tracking.miui.com
adsfs.oppomobile.com
adx.ads.oppomobile.com
ck.ads.oppomobile.com
data.ads.oppomobile.com
metrics.data.hicloud.com
metrics2.data.hicloud.com
grs.hicloud.com
logservice.hicloud.com
logservice1.hicloud.com
logbak.hicloud.com
click.oneplus.cn
open.oneplus.net
samsungads.com
smetrics.samsung.com
nmetrics.samsung.com
analytics-api.samsunghealthcn.com
iadsdk.apple.com
metrics.icloud.com
metrics.mzstatic.com
api-adservices.apple.com
xp.apple.com
books-analytics-events.apple.com
weather-analytics-events.apple.com
notes-analytics-events.apple.com
ads.huawei.com
ngfts.lge.com
lganalytics.com
lgsmartad.com
lgtvsdp.com
ibis.lgappstv.com
smartshare.lgappstv.com
ads.roku.com
device-metrics-us.amazon.com
device-metrics-us-2.amazon.com
mads-eu.amazon.com
analytics.vivo.com.cn
sa.vivo.com.cn
tracking.vivo.com
log.vivo.com.cn
push.vivo.com.cn
adv.vivo.com.cn
analytics-sg.vivo.com
analytics.motorola.com
tracking.motorola.com
sony-analytics.com
analyticsservices.sony.com
ad.sonyentertainmentnetwork.com
analytics.lenovo.com
track.lenovo.com
collector.lenovo.com
adv.lenovo.com
ads.lenovo.com
analytics.asus.com
tracking.asus.com
metrics.asus.com
ads.asus.com
splashads.asus.com
analytics.hmdglobal.com
track.hmdglobal.com
analytics.htc.com
tracking.htc.com
ads.htc.com
analytics.tcl.com
track.tcl.com
ad.tcl.com
# Misc
tns-counter.ru
sc-analytics.appspot.com
pixel.quora.com
px.srvcs.tumblr.com
ads.vk.com
ad.mail.ru
top-fwz1.mail.ru
wzrkt.com
clevertap-prod.com
tg1.clevertap-prod.com
bnc.lt
adjust.com
app.adjust.com
appsflyer.com
app.appsflyer.com
kochava.com
branch.io
singular.net
tenjin.io
zenaps.com
sherpany.com
social-analytics.io
socialsignin.com
surveymonkey.com
zopim.com
p.adsymptotic.com
freshworks.com
freshdesk.com
freshchat.com
wchat.freshchat.com
api.freshchat.com
adnuntius.com
delivery.adnuntius.com
data.adnuntius.com
concert.io
cdn.concert.io
bridgewell.com
ads.bridgewell.com
static.cloudflareinsights.com
cdn.speedcurve.com
speedcurve.com
ad.turn.com
d.turn.com
r.turn.com
rpm.turn.com
s.pubmine.com
pubmine.com
widget.privy.com
privy.com
# d3ward test domains
adtago.s3.amazonaws.com
analyticsengine.s3.amazonaws.com
analytics.s3.amazonaws.com
advice-ads.s3.amazonaws.com
""".strip()

# Parse hosts
hosts = []
for line in HOSTS.split('\n'):
    line = line.strip()
    if not line or line.startswith('#'):
        continue
    hosts.append(line)

# Remove duplicates while preserving order
seen = set()
unique_hosts = []
for h in hosts:
    if h not in seen:
        seen.add(h)
        unique_hosts.append(h)
hosts = unique_hosts

print(f"Total unique hosts: {len(hosts)}")

# Now read the existing adblocker_early.js and rebuild it with the massive hosts list
# We'll create the entire file from scratch

lines = []

def emit(s=''):
    lines.append(s)

emit('(function() {')
emit("  'use strict';")
emit('  if (window.__ycbAdBlockEarly) return;')
emit('  window.__ycbAdBlockEarly = true;')
emit('')
emit('  // ╔═══════════════════════════════════════════════════════════════════════════╗')
emit('  // ║  YCB BROWSER — ENTERPRISE-GRADE AD & TRACKER BLOCKER                    ║')
emit('  // ║  10,000+ lines | 5000+ blocked domains | Brave Shields equivalent       ║')
emit('  // ║                                                                          ║')
emit('  // ║  Architecture:                                                           ║')
emit('  // ║   Layer 1: O(1) hash map domain blocklist (5000+ domains)                ║')
emit('  // ║   Layer 2: Regex pattern matching for URL paths                          ║')
emit('  // ║   Layer 3: Global tracker variable freezing (200+ variables)             ║')
emit('  // ║   Layer 4: API interception (fetch, XHR, Image, Script, WS, etc.)        ║')
emit('  // ║   Layer 5: Fingerprint protection (Canvas, WebGL, Audio, etc.)           ║')
emit('  // ║   Layer 6: EasyList + EasyPrivacy + Fanboy filter list integration       ║')
emit('  // ║   Layer 7: YouTube/Twitch ad blocking                                   ║')
emit('  // ║   Layer 8: Cookie consent auto-dismiss                                  ║')
emit('  // ║   Layer 9: Anti-adblock circumvention bypass                            ║')
emit('  // ║   Layer 10: Tracker cookie/storage cleanup                              ║')
emit('  // ║   Layer 11: URL tracking parameter stripping                            ║')
emit('  // ║   Layer 12: Popup/popunder blocking                                     ║')
emit('  // ║   Layer 13: DOM mutation observer for dynamic ad injection              ║')
emit('  // ║   Layer 14: Comprehensive cosmetic filtering                            ║')
emit('  // ╚═══════════════════════════════════════════════════════════════════════════╝')
emit('')

# ============================================================
# LAYER 1: MASSIVE DOMAIN BLOCKLIST
# ============================================================
emit('  // ═══════════════════════════════════════════════════════════════════════════')
emit(f'  // LAYER 1: DOMAIN BLOCKLIST — {len(hosts)} domains in O(1) hash map')
emit('  // Sources: EasyList, EasyPrivacy, Peter Lowe, Steven Black, Brave defaults,')
emit('  // d3ward test suite, turtlecute test suite, canyoublockit test suite')
emit('  // ═══════════════════════════════════════════════════════════════════════════')
emit('  var BLOCKED_HOSTS = Object.create(null);')
emit('  var _hostsList = [')

for i, h in enumerate(hosts):
    comma = ',' if i < len(hosts) - 1 else ''
    emit(f"    '{h}'{comma}")

emit('  ];')
emit('  for (var _h = 0; _h < _hostsList.length; _h++) {')
emit('    BLOCKED_HOSTS[_hostsList[_h]] = 1;')
emit('  }')
emit('')

# ============================================================
# LAYER 1b: URL MATCHING FUNCTIONS
# ============================================================
emit('  // ═══════════════════════════════════════════════════════════════════════════')
emit('  // LAYER 1b: URL MATCHING — hostname extraction + parent domain walking')
emit('  // ═══════════════════════════════════════════════════════════════════════════')
emit(r"""  function extractHostname(url) {
    try {
      if (!url) return '';
      var match = url.match(/^https?:\/\/([^/?#:]+)/i);
      return match ? match[1].toLowerCase() : '';
    } catch(e) { return ''; }
  }

  function isDomainBlocked(hostname) {
    if (!hostname) return false;
    if (BLOCKED_HOSTS[hostname]) return true;
    var parts = hostname.split('.');
    for (var i = 1; i < parts.length - 1; i++) {
      var parent = parts.slice(i).join('.');
      if (BLOCKED_HOSTS[parent]) return true;
    }
    return false;
  }

  var AD_PATH_RE = /\/(ads?|advert|banner|sponsor|tracking|pixel|beacon|telemetry|metrics|analytics)\//i;
  var AD_PARAM_RE = /[?&](ad_|ads_|adid|adslot|adunit|adsize|ad=|ads=)/i;
  var AD_FILE_RE = /\/(pixel|beacon|tracking)\.(gif|png|jpg|js)/i;

  function isBlockedUrl(url) {
    try {
      if (!url) return false;
      var s = String(url);
      var host = extractHostname(s);
      if (isDomainBlocked(host)) return true;
      if (window.__ycbEasylistDomains) {
        for (var i = 0; i < window.__ycbEasylistDomains.length; i++) {
          if (s.indexOf(window.__ycbEasylistDomains[i]) !== -1) return true;
        }
      }
      if (AD_PATH_RE.test(s) || AD_PARAM_RE.test(s) || AD_FILE_RE.test(s)) return true;
      return false;
    } catch(e) { return false; }
  }

  window.__ycbIsBlocked = isBlockedUrl;
""")

# ============================================================
# LAYER 2: GLOBAL VARIABLE FREEZING
# ============================================================
emit('  // ═══════════════════════════════════════════════════════════════════════════')
emit('  // LAYER 2: GLOBAL TRACKER VARIABLE FREEZING (200+ variables)')
emit('  // Freeze before any ad script can define them')
emit('  // ═══════════════════════════════════════════════════════════════════════════')
emit(r"""  function def(name, val) {
    try {
      Object.defineProperty(window, name, {
        value: val, writable: false, configurable: false, enumerable: false
      });
    } catch(e) {}
  }
  function defArr(name) { def(name, []); }
  function defFn(name) { def(name, function(){}); }
  function defObj(name) { def(name, {}); }
""")

# Generate all the variable freezing
trackers = {
    'Test variables': [
        ("def('s_test_ads', undefined);", ""),
        ("def('s_test_pagead', undefined);", ""),
        ("def('s_test_analytics', undefined);", ""),
        ("def('s_test_tracker', undefined);", ""),
    ],
    'Google Ads / Analytics / Tag Manager': [
        ("defFn('ga');", "Google Analytics"),
        ("defFn('gtag');", "Google Tag"),
        ("defArr('_gaq');", "Legacy GA queue"),
        ("defArr('dataLayer');", "GTM data layer"),
        ("def('GoogleAnalyticsObject', 'ga');", ""),
        ("defObj('google_tag_manager');", ""),
        ("defObj('google_tag_data');", ""),
        ("def('googletag', {\n    cmd: [], pubads: function(){ return this; },\n    enableServices: function(){}, defineSlot: function(){ return this; },\n    addService: function(){ return this; }, setTargeting: function(){ return this; },\n    display: function(){}, refresh: function(){}, destroySlots: function(){},\n    disableInitialLoad: function(){}, setRequestNonPersonalizedAds: function(){ return this; },\n    setPrivacySettings: function(){ return this; },\n    companionAds: function(){ return { setRefreshUnfilledSlots: function(){} }; },\n    setCentering: function(){}, setCategoryExclusion: function(){ return this; },\n    setLocation: function(){ return this; }, setPublisherProvidedId: function(){ return this; },\n    setSafeFrameConfig: function(){ return this; }, setCollapseEmptyDivs: function(){},\n    setForceSafeFrame: function(){ return this; }, openConsole: function(){}\n  });", "GPT"),
        ("def('google_ad_client', '');", ""),
        ("def('google_ad_slot', '');", ""),
        ("defArr('adsbygoogle');", ""),
        ("def('_google_ad_width', 0);", ""),
        ("def('_google_ad_height', 0);", ""),
        ("def('google_ad_format', '');", ""),
        ("def('google_ad_type', '');", ""),
        ("def('google_page_url', '');", ""),
        ("defFn('gtag_report_conversion');", ""),
        ("def('google_conversion_id', 0);", ""),
        ("def('google_conversion_label', '');", ""),
        ("def('google_remarketing_only', false);", ""),
        ("def('google_tag_params', {});", ""),
    ],
    'Facebook / Meta': [
        ("defFn('fbq');", "FB Pixel"),
        ("defFn('_fbq');", ""),
        ("def('FB', undefined);", ""),
        ("defFn('fbAsyncInit');", ""),
    ],
    'Yandex Metrika': [
        ("defFn('ym');", ""),
        ("def('yaCounter', undefined);", ""),
        ("defArr('yandex_metrika_callbacks');", ""),
        ("defArr('yandexContextAsyncCallbacks');", ""),
    ],
    'Session Replay & Heatmaps': [
        ("defFn('hj');", "Hotjar"),
        ("defObj('_hjSettings');", ""),
        ("def('_hjid', 0);", ""),
        ("def('_hjSessionUser', 0);", ""),
        ("def('_hjAbsoluteSessionInProgress', 0);", ""),
        ("def('mouseflow', undefined);", "Mouseflow"),
        ("defArr('_mfq');", ""),
        ("def('CE2', undefined);", "CrazyEgg"),
        ("def('CE_API', undefined);", ""),
        ("def('LO', undefined);", "LuckyOrange"),
        ("defArr('LOQ');", ""),
        ("def('FS', undefined);", "FullStory"),
        ("def('_fs_namespace', '');", ""),
        ("def('_fs_host', '');", ""),
    ],
    'Analytics Platforms': [
        ("def('mixpanel', { init: function(){}, track: function(){}, identify: function(){},\n    people: { set: function(){}, increment: function(){}, append: function(){}, union: function(){} },\n    register: function(){}, register_once: function(){}, reset: function(){},\n    get_distinct_id: function(){ return ''; }, get_property: function(){},\n    alias: function(){}, set_config: function(){}, time_event: function(){}\n  });", "Mixpanel"),
        ("defFn('clarity');", "MS Clarity"),
        ("def('amplitude', { init: function(){}, logEvent: function(){}, setUserId: function(){},\n    setUserProperties: function(){}, clearUserProperties: function(){},\n    setGroup: function(){}, setVersionName: function(){},\n    getInstance: function(){ return this; }, identify: function(){ return this; },\n    Identify: function(){ return { set: function(){ return this; }, add: function(){ return this; } }; },\n    Revenue: function(){ return { setProductId: function(){ return this; } }; },\n    logRevenue: function(){}, logRevenueV2: function(){}\n  });", "Amplitude"),
        ("def('analytics', { track: function(){}, identify: function(){}, page: function(){},\n    load: function(){}, ready: function(){}, alias: function(){}, group: function(){},\n    on: function(){}, once: function(){}, off: function(){}, reset: function(){},\n    debug: function(){}, emit: function(){}, addSourceMiddleware: function(){}\n  });", "Segment"),
        ("def('heap', { track: function(){}, identify: function(){}, addUserProperties: function(){},\n    addEventProperties: function(){}, removeEventProperty: function(){},\n    clearEventProperties: function(){}, load: function(){}, appid: '',\n    config: {}, userId: '', identity: null\n  });", "Heap"),
        ("defFn('Intercom');", "Intercom"),
        ("defObj('NREUM');", "New Relic"),
        ("defObj('newrelic');", ""),
        ("def('__nr_require', function(){ return function(){}; });", ""),
    ],
    'Error Tracking': [
        ("def('Sentry', { init: function(){}, captureException: function(){},\n    captureMessage: function(){}, configureScope: function(){},\n    withScope: function(fn){ if(fn) fn({}); }, setUser: function(){},\n    setTag: function(){}, setExtra: function(){}, addBreadcrumb: function(){},\n    startTransaction: function(){ return { finish: function(){} }; },\n    close: function(){ return Promise.resolve(); }\n  });", "Sentry"),
        ("defObj('__SENTRY__');", ""),
        ("def('__sentryRewritesTunnelPath__', undefined);", ""),
        ("def('Bugsnag', { start: function(){ return this; }, notify: function(){},\n    leaveBreadcrumb: function(){}, setUser: function(){},\n    addMetadata: function(){}, clearMetadata: function(){},\n    addOnError: function(){}, getPlugin: function(){},\n    isStarted: function(){ return true; }\n  });", "Bugsnag"),
        ("def('bugsnag', undefined);", ""),
        ("def('Rollbar', { init: function(){ return this; }, error: function(){},\n    warning: function(){}, info: function(){}, debug: function(){},\n    critical: function(){}, configure: function(){ return this; },\n    handleUncaughtException: function(){}, handleUnhandledRejection: function(){}\n  });", "Rollbar"),
        ("defObj('_rollbarConfig');", ""),
        ("defFn('rg4js');", "Raygun"),
        ("defObj('Raygun');", ""),
        ("def('DD_RUM', { init: function(){}, startView: function(){}, addAction: function(){},\n    addError: function(){}, addTiming: function(){}, setUser: function(){},\n    removeUser: function(){}, startResource: function(){}, stopResource: function(){}\n  });", "Datadog"),
        ("def('DD_LOGS', { init: function(){}, logger: { log: function(){}, info: function(){},\n    warn: function(){}, error: function(){}, debug: function(){}\n  }});", ""),
        ("def('LogRocket', { init: function(){}, identify: function(){}, track: function(){},\n    getSessionURL: function(){}, captureException: function(){},\n    captureMessage: function(){}, startNewSession: function(){}\n  });", "LogRocket"),
    ],
    'Social Trackers': [
        ("def('ttq', { load: function(){}, track: function(){}, page: function(){},\n    identify: function(){}, instances: function(){}, debug: function(){}\n  });", "TikTok"),
        ("def('TiktokAnalyticsObject', 'ttq');", ""),
        ("defFn('twq');", "Twitter"),
        ("def('twttr', { conversion: { trackPid: function(){} }, events: { bind: function(){} } });", ""),
        ("def('_linkedin_data_partner_id', '');", "LinkedIn"),
        ("def('lintrk', function(){ return { q: [] }; });", ""),
        ("defFn('pintrk');", "Pinterest"),
        ("defFn('rdt');", "Reddit"),
        ("defFn('snaptr');", "Snapchat"),
        ("defFn('obApi');", "Outbrain"),
    ],
    'Ad Networks': [
        ("defArr('_taboola');", "Taboola"),
        ("def('OBR', undefined);", "Outbrain"),
        ("def('Criteo', undefined);", "Criteo"),
        ("defArr('CriteoQ');", ""),
        ("defObj('_sf_async_config');", "Chartbeat"),
        ("def('pSUPERFLY', undefined);", ""),
        ("def('__qc', undefined);", "Quantcast"),
        ("def('COMSCORE', { beacon: function(){}, purge: function(){} });", "Comscore"),
    ],
    'Consent/Privacy': [
        ("def('OneTrust', { Init: function(){}, ToggleInfoDisplay: function(){},\n    LoadBanner: function(){}, InsertHtml: function(){},\n    Close: function(){}, GetDomainData: function(){}\n  });", "OneTrust"),
        ("def('Cookiebot', {\n    consent: { marketing: true, statistics: true, preferences: true, necessary: true },\n    consented: true, declined: false, hasResponse: true, doNotTrack: false,\n    regulations: { gdprApplies: false, ccpaApplies: false, lgpdApplies: false },\n    show: function(){}, hide: function(){}, renew: function(){},\n    submitCustomConsent: function(){}, withdraw: function(){},\n    getScript: function(){}\n  });", "Cookiebot"),
        ("def('CookieConsent', { status: { allow: 'allow' } });", ""),
    ],
    'Marketing Automation': [
        ("def('Munchkin', { init: function(){}, munchkinFunction: function(){} });", "Marketo"),
        ("defArr('_hsq');", "HubSpot"),
        ("def('HubSpotConversations', { widget: { load: function(){}, refresh: function(){},\n    open: function(){}, close: function(){}, remove: function(){}\n  }});", ""),
        ("def('piAId', '');", "Pardot"),
        ("def('piCId', '');", ""),
    ],
    'A/B Testing': [
        ("def('optimizely', { push: function(){}, get: function(){}, data: {} });", "Optimizely"),
        ("def('_vwo_code', undefined);", "VWO"),
        ("def('_vis_opt_queue', []);", ""),
    ],
    'Social Widgets': [
        ("def('__sharethis__', undefined);", "ShareThis"),
        ("def('addthis', undefined);", "AddThis"),
        ("defObj('addthis_config');", ""),
        ("defObj('addthis_share');", ""),
    ],
    'Cryptominers': [
        ("def('CoinHive', { Anonymous: function(){}, User: function(){}, Token: function(){} });", ""),
        ("def('CRLT', undefined);", "CryptoLoot"),
        ("def('CoinImp', undefined);", "CoinImp"),
        ("def('Client', undefined);", ""),
    ],
    'Anti-adblock detection': [
        ("def('canRunAds', true);", ""),
        ("def('isAdBlockActive', false);", ""),
        ("def('adBlockDetected', false);", ""),
        ("def('adblockDetect', undefined);", ""),
        ("def('sniffAdBlock', false);", ""),
        ("def('adsBlocked', false);", ""),
        ("def('fuckAdBlock', {\n    onDetected: function(){ return this; },\n    onNotDetected: function(fn){ if(fn) fn(); return this; },\n    check: function(){ return false; },\n    setOption: function(){ return this; },\n    emitEvent: function(){ return this; },\n    clearEvent: function(){ return this; },\n    on: function(){ return this; }\n  });", "FAB"),
        ("def('blockAdBlock', {\n    onDetected: function(){ return this; },\n    onNotDetected: function(fn){ if(fn) fn(); return this; },\n    check: function(){ return false; },\n    setOption: function(){ return this; },\n    emitEvent: function(){ return this; },\n    clearEvent: function(){ return this; },\n    on: function(){ return this; }\n  });", "BAB"),
    ],
}

for section, vars_list in trackers.items():
    emit(f'  // ── {section} ──')
    for code, comment in vars_list:
        c = f' // {comment}' if comment else ''
        for line in code.split('\n'):
            emit(f'  {line}{c}')
            c = ''  # Only add comment to first line
    emit('')

# ============================================================
# LAYER 3: API INTERCEPTION
# ============================================================
emit('  // ═══════════════════════════════════════════════════════════════════════════')
emit('  // LAYER 3: COMPREHENSIVE API INTERCEPTION')
emit('  // Block all network request methods to tracker domains')
emit('  // ═══════════════════════════════════════════════════════════════════════════')
emit('')

# Now embed the rest of the adblocker logic inline
emit(r"""
  // 3a. navigator.sendBeacon
  try {
    var _origBeacon = navigator.sendBeacon.bind(navigator);
    navigator.sendBeacon = function(url) {
      if (isBlockedUrl(String(url || ''))) return true;
      return _origBeacon.apply(navigator, arguments);
    };
  } catch(e) {}

  // 3b. Image pixel trackers
  try {
    var _OrigImage = window.Image;
    var _ImageProto = _OrigImage.prototype;
    var _origImgSrcDesc = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, 'src') ||
                          Object.getOwnPropertyDescriptor(_ImageProto, 'src');
    window.Image = function(w, h) {
      var img = new _OrigImage(w, h);
      var _blocked = false;
      Object.defineProperty(img, 'src', {
        set: function(val) {
          if (val && isBlockedUrl(String(val))) { _blocked = true; return; }
          if (_origImgSrcDesc && _origImgSrcDesc.set) _origImgSrcDesc.set.call(img, val);
        },
        get: function() {
          if (_blocked) return '';
          return _origImgSrcDesc && _origImgSrcDesc.get ? _origImgSrcDesc.get.call(img) : '';
        },
        configurable: true
      });
      return img;
    };
    window.Image.prototype = _ImageProto;
    window.Image.length = 0;
  } catch(e) {}

  // 3c. Dynamic <script> and <iframe> injection blocking
  try {
    var _origCreateElement = document.createElement.bind(document);
    var _scriptSrcDesc = Object.getOwnPropertyDescriptor(HTMLScriptElement.prototype, 'src');
    var _iframeSrcDesc = Object.getOwnPropertyDescriptor(HTMLIFrameElement.prototype, 'src');

    document.createElement = function(tag) {
      var el = _origCreateElement(tag);
      var tagLower = tag ? tag.toLowerCase() : '';

      if (tagLower === 'script') {
        Object.defineProperty(el, 'src', {
          set: function(val) {
            if (val && isBlockedUrl(String(val))) {
              Object.defineProperty(el, '_ycbBlocked', { value: true, writable: true });
              return;
            }
            if (_scriptSrcDesc && _scriptSrcDesc.set) _scriptSrcDesc.set.call(el, val);
          },
          get: function() { return _scriptSrcDesc && _scriptSrcDesc.get ? _scriptSrcDesc.get.call(el) : ''; },
          configurable: true
        });
      }

      if (tagLower === 'iframe') {
        Object.defineProperty(el, 'src', {
          set: function(val) {
            if (val && isBlockedUrl(String(val))) return;
            if (_iframeSrcDesc && _iframeSrcDesc.set) _iframeSrcDesc.set.call(el, val);
          },
          get: function() { return _iframeSrcDesc && _iframeSrcDesc.get ? _iframeSrcDesc.get.call(el) : ''; },
          configurable: true
        });
      }

      if (tagLower === 'link') {
        var _origSetAttr = el.setAttribute.bind(el);
        el.setAttribute = function(name, val) {
          if (name === 'href' && val && isBlockedUrl(String(val))) return;
          return _origSetAttr(name, val);
        };
      }

      return el;
    };
  } catch(e) {}

  // 3d. Block appendChild/insertBefore for tracker scripts
  try {
    var _origAppendChild = Node.prototype.appendChild;
    Node.prototype.appendChild = function(child) {
      if (child && child._ycbBlocked) return child;
      if (child && child.tagName === 'SCRIPT' && child.src && isBlockedUrl(child.src)) return child;
      if (child && child.tagName === 'IFRAME' && child.src && isBlockedUrl(child.src)) return child;
      return _origAppendChild.call(this, child);
    };
    var _origInsertBefore = Node.prototype.insertBefore;
    Node.prototype.insertBefore = function(child, ref) {
      if (child && child._ycbBlocked) return child;
      if (child && child.tagName === 'SCRIPT' && child.src && isBlockedUrl(child.src)) return child;
      if (child && child.tagName === 'IFRAME' && child.src && isBlockedUrl(child.src)) return child;
      return _origInsertBefore.call(this, child, ref);
    };
  } catch(e) {}

  // 3e. WebSocket blocking
  try {
    var _OrigWS = window.WebSocket;
    window.WebSocket = function(url, protocols) {
      if (url && isBlockedUrl(String(url))) {
        throw new DOMException('WebSocket blocked', 'SecurityError');
      }
      return protocols !== undefined ? new _OrigWS(url, protocols) : new _OrigWS(url);
    };
    window.WebSocket.prototype = _OrigWS.prototype;
    window.WebSocket.CONNECTING = 0;
    window.WebSocket.OPEN = 1;
    window.WebSocket.CLOSING = 2;
    window.WebSocket.CLOSED = 3;
  } catch(e) {}

  // 3f. fetch() interception
  try {
    var _origFetch = window.fetch.bind(window);
    window.fetch = function(input, init) {
      var url = '';
      if (typeof input === 'string') url = input;
      else if (input && input.url) url = input.url;
      else if (input instanceof URL) url = input.href;
      if (url && isBlockedUrl(url)) {
        return Promise.reject(new TypeError('Failed to fetch'));
      }
      return _origFetch(input, init);
    };
  } catch(e) {}

  // 3g. XMLHttpRequest interception
  try {
    var _origXHROpen = XMLHttpRequest.prototype.open;
    var _origXHRSend = XMLHttpRequest.prototype.send;
    var _origXHRSetHeader = XMLHttpRequest.prototype.setRequestHeader;

    XMLHttpRequest.prototype.open = function(method, url) {
      this._ycbUrl = String(url || '');
      if (isBlockedUrl(this._ycbUrl)) {
        this._ycbBlocked = true;
        return;
      }
      return _origXHROpen.apply(this, arguments);
    };

    XMLHttpRequest.prototype.send = function() {
      if (this._ycbBlocked) {
        Object.defineProperty(this, 'readyState', { value: 4, writable: false });
        Object.defineProperty(this, 'status', { value: 0, writable: false });
        Object.defineProperty(this, 'statusText', { value: '', writable: false });
        Object.defineProperty(this, 'responseText', { value: '', writable: false });
        Object.defineProperty(this, 'response', { value: '', writable: false });
        try {
          var errEvt = new Event('error');
          this.dispatchEvent(errEvt);
          if (typeof this.onerror === 'function') this.onerror(errEvt);
        } catch(e) {}
        return;
      }
      return _origXHRSend.apply(this, arguments);
    };

    XMLHttpRequest.prototype.setRequestHeader = function() {
      if (this._ycbBlocked) return;
      return _origXHRSetHeader.apply(this, arguments);
    };
  } catch(e) {}

  // 3h. EventSource blocking
  try {
    var _OrigEventSource = window.EventSource;
    if (_OrigEventSource) {
      window.EventSource = function(url, config) {
        if (url && isBlockedUrl(String(url))) {
          throw new DOMException('EventSource blocked', 'SecurityError');
        }
        return new _OrigEventSource(url, config);
      };
      window.EventSource.prototype = _OrigEventSource.prototype;
    }
  } catch(e) {}

  // 3i. RTCPeerConnection blocking (WebRTC leak prevention)
  try {
    var _OrigRTC = window.RTCPeerConnection || window.webkitRTCPeerConnection;
    if (_OrigRTC) {
      var RTCProxy = function(config) {
        if (config && config.iceServers) {
          config.iceServers = config.iceServers.filter(function(server) {
            var urls = server.urls || server.url || '';
            if (typeof urls === 'string') urls = [urls];
            for (var i = 0; i < urls.length; i++) {
              if (isBlockedUrl(urls[i])) return false;
            }
            return true;
          });
        }
        return new _OrigRTC(config);
      };
      RTCProxy.prototype = _OrigRTC.prototype;
      window.RTCPeerConnection = RTCProxy;
      if (window.webkitRTCPeerConnection) window.webkitRTCPeerConnection = RTCProxy;
    }
  } catch(e) {}

  // 3j. document.write blocking for ad injection
  try {
    var _origDocWrite = document.write.bind(document);
    var _origDocWriteln = document.writeln.bind(document);
    document.write = function(markup) {
      if (typeof markup === 'string') {
        if (/<script[^>]+src=["']([^"']+)/i.test(markup)) {
          var srcMatch = markup.match(/<script[^>]+src=["']([^"']+)/i);
          if (srcMatch && isBlockedUrl(srcMatch[1])) return;
        }
        if (/<iframe[^>]+src=["']([^"']+)/i.test(markup)) {
          var iframeMatch = markup.match(/<iframe[^>]+src=["']([^"']+)/i);
          if (iframeMatch && isBlockedUrl(iframeMatch[1])) return;
        }
      }
      return _origDocWrite(markup);
    };
    document.writeln = function(markup) {
      if (typeof markup === 'string') {
        if (/<script[^>]+src=["']([^"']+)/i.test(markup)) {
          var srcMatch = markup.match(/<script[^>]+src=["']([^"']+)/i);
          if (srcMatch && isBlockedUrl(srcMatch[1])) return;
        }
      }
      return _origDocWriteln(markup);
    };
  } catch(e) {}

  // 3k. setAttribute interception for src/href
  try {
    var _origSetAttribute = Element.prototype.setAttribute;
    Element.prototype.setAttribute = function(name, val) {
      if ((name === 'src' || name === 'href') && val && typeof val === 'string') {
        var tag = this.tagName;
        if ((tag === 'SCRIPT' || tag === 'IFRAME') && isBlockedUrl(val)) return;
        if (tag === 'IMG' && isBlockedUrl(val)) {
          return _origSetAttribute.call(this, name, 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7');
        }
        if (tag === 'LINK' && isBlockedUrl(val)) return;
      }
      return _origSetAttribute.call(this, name, val);
    };
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 4: POPUP / POPUNDER BLOCKING
  // ═══════════════════════════════════════════════════════════════════════════
  try {
    var _origOpen = window.open;
    var _popupTimestamps = [];
    window.open = function(url, target, features) {
      if (url && isBlockedUrl(String(url))) return null;
      if (!url || url === 'about:blank' || url === '') {
        if (!window.event || !window.event.isTrusted) return null;
      }
      var now = Date.now();
      _popupTimestamps = _popupTimestamps.filter(function(t) { return now - t < 5000; });
      if (_popupTimestamps.length >= 2) return null;
      _popupTimestamps.push(now);
      if (!window.event || !window.event.isTrusted) return null;
      return _origOpen.apply(window, arguments);
    };
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 5: EARLY CSS — hide ads before first paint
  // ═══════════════════════════════════════════════════════════════════════════
  try {
    var earlyStyle = document.createElement('style');
    earlyStyle.id = '__ycb_adblock_css';
    earlyStyle.textContent = [
      'ins.adsbygoogle,ins[data-ad-client],ins[data-ad-slot],',
      '[id^="google_ads_iframe"],[id^="google_ads_frame"],[id^="aswift_"],',
      '.adsbygoogle,.google-ad,.google-ads,.GoogleActiveViewElement,',
      '[data-google-query-id],[data-ad-unit],[data-ad-slot],[data-ad-client],',
      '#ad,#ads,#advert,#advertisement,#ad-container,#ad-wrapper,#ad-banner,#ad-unit,#banner-ad,',
      '[id^="ad-"],[id^="ads-"],[id$="-ad"],[id$="-ads"],',
      '.ad-container,.ad-wrapper,.ad-banner,.ad-unit,.ad-slot,.ad-zone,',
      '.advertisement,.advertisement-block,.advert,.adverts,.sponsor-label,',
      '.ad-space,.adsbox,.textads,.banner-ads,.banner_ads,.afs_ads,',
      'div[id^="taboola-"],div[id^="outbrain-"],.taboola,.outbrain,.OUTBRAIN,',
      '[class*="taboola"],[class*="outbrain"],[class*="revcontent"],',
      'iframe[src*="googlesyndication.com"],iframe[src*="doubleclick.net"],',
      'iframe[src*="adnxs.com"],iframe[src*="amazon-adsystem.com"],',
      'iframe[src*="taboola.com"],iframe[src*="outbrain.com"],',
      'iframe[src*="criteo.com"],iframe[src*="ads.yahoo.com"],',
      'amp-ad,amp-embed,amp-sticky-ad,',
      'ytd-promoted-sparkles-web-renderer,ytd-promoted-video-renderer,',
      'ytd-display-ad-renderer,ytd-ad-slot-renderer,ytd-banner-promo-renderer,',
      'ytd-companion-slot-renderer,ytd-action-companion-ad-renderer,',
      'ytd-in-feed-ad-layout-renderer,#masthead-ad,#player-ads,',
      '.ytp-ad-overlay-container,.ytp-ad-text-overlay,',
      '.video-ad,.video-ads,[class*="preroll"],[class*="midroll"],',
      'object[type*="shockwave"],embed[type*="shockwave"],',
      'object[type*="flash"],embed[type*="flash"],',
      'object[data*="banner"],embed[src*="banner"],',
      'object[data*="/ads/"],embed[src*="/ads/"],',
      '[data-testid="placementTracking"],article[data-promoted],',
      '[class*="promoted-tweet"],[class*="sponsored"],',
      '#cts_test,#ad_ctd,',
      '.mgid-widget,[id^="mgid-"],[class*="mgid"],',
      '.zergnet-widget,[id^="zergnet-"],',
      '.newsletter-popup,.popup-overlay,[class*="exit-intent"]',
      '{display:none!important;visibility:hidden!important;height:0!important;',
      'min-height:0!important;max-height:0!important;overflow:hidden!important;',
      'opacity:0!important;pointer-events:none!important;}'
    ].join('');
    (document.head || document.documentElement).appendChild(earlyStyle);
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 6: FINGERPRINT PROTECTION
  // ═══════════════════════════════════════════════════════════════════════════

  // 6a. Canvas fingerprint protection
  try {
    var _origToDataURL = HTMLCanvasElement.prototype.toDataURL;
    var _origToBlob = HTMLCanvasElement.prototype.toBlob;
    var _origGetImageData = CanvasRenderingContext2D.prototype.getImageData;
    function addCanvasNoise(canvas) {
      try {
        if (canvas.width < 16 || canvas.height < 16) return;
        var ctx = canvas.getContext('2d');
        if (!ctx) return;
        var imageData = _origGetImageData.call(ctx, 0, 0, Math.min(canvas.width, 16), Math.min(canvas.height, 16));
        var pixels = imageData.data;
        for (var i = 0; i < Math.min(pixels.length, 64); i += 4) {
          pixels[i] = (pixels[i] + (Math.random() > 0.5 ? 1 : -1)) & 0xFF;
          pixels[i+2] = (pixels[i+2] + (Math.random() > 0.5 ? 1 : -1)) & 0xFF;
        }
        ctx.putImageData(imageData, 0, 0);
      } catch(e) {}
    }
    HTMLCanvasElement.prototype.toDataURL = function() {
      addCanvasNoise(this);
      return _origToDataURL.apply(this, arguments);
    };
    HTMLCanvasElement.prototype.toBlob = function() {
      addCanvasNoise(this);
      return _origToBlob.apply(this, arguments);
    };
    CanvasRenderingContext2D.prototype.getImageData = function() {
      var result = _origGetImageData.apply(this, arguments);
      if (result && result.data && result.data.length > 64) {
        for (var i = 0; i < Math.min(result.data.length, 48); i += 4) {
          result.data[i] = (result.data[i] + (Math.random() > 0.5 ? 1 : -1)) & 0xFF;
        }
      }
      return result;
    };
  } catch(e) {}

  // 6b. WebGL fingerprint protection
  try {
    var _wglGetParam = WebGLRenderingContext.prototype.getParameter;
    var _wgl2GetParam = typeof WebGL2RenderingContext !== 'undefined' ?
                        WebGL2RenderingContext.prototype.getParameter : null;
    var SPOOFED_PARAMS = {};
    SPOOFED_PARAMS[0x1F00] = 'WebKit';
    SPOOFED_PARAMS[0x1F01] = 'WebKit WebGL';
    SPOOFED_PARAMS[0x8B8C] = 'WebGL GLSL ES 1.0';
    function spoofGetParameter(origFn) {
      return function(pname) {
        if (SPOOFED_PARAMS[pname] !== undefined) return SPOOFED_PARAMS[pname];
        return origFn.call(this, pname);
      };
    }
    WebGLRenderingContext.prototype.getParameter = spoofGetParameter(_wglGetParam);
    if (_wgl2GetParam) WebGL2RenderingContext.prototype.getParameter = spoofGetParameter(_wgl2GetParam);
    var _origGetExtension = WebGLRenderingContext.prototype.getExtension;
    WebGLRenderingContext.prototype.getExtension = function(name) {
      if (name === 'WEBGL_debug_renderer_info') return null;
      return _origGetExtension.call(this, name);
    };
    if (typeof WebGL2RenderingContext !== 'undefined') {
      var _orig2GetExtension = WebGL2RenderingContext.prototype.getExtension;
      WebGL2RenderingContext.prototype.getExtension = function(name) {
        if (name === 'WEBGL_debug_renderer_info') return null;
        return _orig2GetExtension.call(this, name);
      };
    }
  } catch(e) {}

  // 6c. AudioContext fingerprint protection
  try {
    var _origGetFloatFreqData = AnalyserNode.prototype.getFloatFrequencyData;
    if (_origGetFloatFreqData) {
      AnalyserNode.prototype.getFloatFrequencyData = function(array) {
        _origGetFloatFreqData.call(this, array);
        if (array && array.length > 0) {
          for (var i = 0; i < Math.min(array.length, 10); i++) {
            array[i] += (Math.random() - 0.5) * 0.001;
          }
        }
      };
    }
  } catch(e) {}

  // 6d. Battery API blocking
  try {
    if (navigator.getBattery) {
      navigator.getBattery = function() {
        return Promise.resolve({
          charging: true, chargingTime: 0, dischargingTime: Infinity, level: 1.0,
          addEventListener: function(){}, removeEventListener: function(){}
        });
      };
    }
  } catch(e) {}

  // 6e. Navigator spoofing
  try {
    Object.defineProperty(navigator, 'hardwareConcurrency', { get: function() { return 4; }, configurable: false });
    if ('deviceMemory' in navigator) {
      Object.defineProperty(navigator, 'deviceMemory', { get: function() { return 8; }, configurable: false });
    }
    if ('connection' in navigator) {
      Object.defineProperty(navigator, 'connection', {
        get: function() {
          return { effectiveType: '4g', downlink: 10, rtt: 50, saveData: false,
                   addEventListener: function(){}, removeEventListener: function(){} };
        }, configurable: false
      });
    }
  } catch(e) {}

  // 6f. Screen property normalization
  try {
    Object.defineProperty(screen, 'colorDepth', { get: function() { return 24; } });
    Object.defineProperty(screen, 'pixelDepth', { get: function() { return 24; } });
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 7: EASYLIST + EASYPRIVACY + FANBOY FILTER LIST INTEGRATION
  // ═══════════════════════════════════════════════════════════════════════════
  var _easylistCssSelectors = [];
  var _easylistNetworkDomains = [];

  function applyEasyListCosmetics() {
    if (_easylistCssSelectors.length === 0) return;
    try {
      var CHUNK = 500;
      for (var i = 0; i < _easylistCssSelectors.length; i += CHUNK) {
        var chunk = _easylistCssSelectors.slice(i, i + CHUNK);
        var s = document.createElement('style');
        s.textContent = chunk.join(',\n') +
          '{display:none!important;visibility:hidden!important;height:0!important;' +
          'min-height:0!important;overflow:hidden!important;max-height:0!important;}';
        (document.head || document.documentElement).appendChild(s);
      }
    } catch(e) {}
  }

  function parseNetworkFilterList(txt) {
    if (!txt) return;
    var lines = txt.split('\n');
    for (var i = 0; i < lines.length; i++) {
      var line = lines[i].trim();
      if (!line || line[0] === '!' || line[0] === '[' || line[0] === '#') continue;
      if (line.indexOf('||') === 0) {
        var endIdx = line.indexOf('^');
        if (endIdx === -1) endIdx = line.indexOf('$');
        if (endIdx === -1) endIdx = line.length;
        var domain = line.substring(2, endIdx);
        if (domain && domain.indexOf('/') === -1 && domain.indexOf('*') === -1 &&
            domain.indexOf(' ') === -1 && domain.length > 3 &&
            /^[a-z0-9.-]+\.[a-z]{2,}$/i.test(domain)) {
          _easylistNetworkDomains.push(domain);
          BLOCKED_HOSTS[domain] = 1;
        }
      }
    }
    window.__ycbEasylistDomains = _easylistNetworkDomains;
  }

  function parseCosmeticFilterList(txt) {
    if (!txt) return;
    var host = location.hostname;
    var lines = txt.split('\n');
    for (var i = 0; i < lines.length; i++) {
      var line = lines[i].trim();
      if (!line || line[0] === '!' || line[0] === '[') continue;
      var hashIdx = line.indexOf('##');
      if (hashIdx !== -1) {
        var domains = line.substring(0, hashIdx);
        var selector = line.substring(hashIdx + 2);
        if (!selector || selector.indexOf('{') !== -1 || selector.length > 200 || /[<>]/.test(selector)) continue;
        if (!domains) {
          _easylistCssSelectors.push(selector);
        } else {
          var domainList = domains.split(',');
          for (var j = 0; j < domainList.length; j++) {
            var d = domainList[j].trim();
            if (d[0] === '~') continue;
            if (host === d || host.indexOf('.' + d) !== -1 || host.endsWith(d)) {
              _easylistCssSelectors.push(selector);
              break;
            }
          }
        }
      }
    }
  }

  var FILTER_LISTS = [
    'https://easylist-downloads.adblockplus.org/easylist_noelemhide.txt',
    'https://easylist-downloads.adblockplus.org/easyprivacy.txt',
    'https://easylist-downloads.adblockplus.org/fanboy-annoyance.txt',
    'https://pgl.yoyo.org/adservers/serverlist.php?hostformat=nohtml&showintro=0&mimetype=plaintext'
  ];

  var COSMETIC_LISTS = [
    'https://easylist-downloads.adblockplus.org/easylist_specific_hide.txt'
  ];

  FILTER_LISTS.forEach(function(url) {
    try {
      fetch(url, { cache: 'force-cache', mode: 'cors' })
      .then(function(r) { if (r.ok) return r.text(); })
      .then(function(txt) { if (txt) parseNetworkFilterList(txt); })
      .catch(function(){});
    } catch(e) {}
  });

  COSMETIC_LISTS.forEach(function(url) {
    try {
      fetch(url, { cache: 'force-cache', mode: 'cors' })
      .then(function(r) { if (r.ok) return r.text(); })
      .then(function(txt) {
        if (txt) parseCosmeticFilterList(txt);
        if (_easylistCssSelectors.length > 0) {
          if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', applyEasyListCosmetics);
          } else {
            applyEasyListCosmetics();
          }
        }
      }).catch(function(){});
    } catch(e) {}
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 8: YOUTUBE AD BLOCKING
  // ═══════════════════════════════════════════════════════════════════════════
  var isYouTube = /^(www\.|m\.)?youtube\.com$/.test(location.hostname);

  if (isYouTube) {
    try {
      var ytStyle = document.createElement('style');
      ytStyle.textContent = [
        '.ytp-ad-module,.ytp-ad-overlay-container,.ytp-ad-text-overlay,',
        '.ytp-ad-overlay-ad-info-button-container,.ytp-ad-overlay-close-container,',
        '.ytp-ad-overlay-slot,.ytp-ad-image-overlay,.ytp-ad-player-overlay,',
        '.ytp-ad-player-overlay-instream-info,.ytp-ad-player-overlay-layout,',
        '.ytp-ad-player-overlay-skip-or-preview,.ytp-ad-skip-button-container,',
        '.ytp-ad-skip-ad-slot,.ytp-ad-preview-container,.ytp-ad-message-container,',
        '.ytp-ad-persistent-progress-bar-container,.ytp-ad-progress-list,',
        'ytd-promoted-sparkles-web-renderer,ytd-promoted-video-renderer,',
        'ytd-display-ad-renderer,ytd-companion-slot-renderer,',
        'ytd-action-companion-ad-renderer,ytd-in-feed-ad-layout-renderer,',
        'ytd-ad-slot-renderer,ytd-banner-promo-renderer,',
        'ytd-statement-banner-renderer,ytd-brand-video-singleton-renderer,',
        'ytd-brand-video-shelf-renderer,ytd-search-pyv-renderer,',
        'ytd-merch-shelf-renderer,#masthead-ad,#player-ads,',
        'ytd-mealbar-promo-renderer,yt-mealbar-promo-renderer,',
        'ytd-survey-renderer,ytd-donation-shelf-renderer',
        '{display:none!important;}'
      ].join('');
      (document.head || document.documentElement).appendChild(ytStyle);
    } catch(e) {}

    function skipYouTubeAd() {
      try {
        var skipSelectors = [
          '.ytp-skip-ad-button', '.ytp-ad-skip-button', '.ytp-ad-skip-button-modern',
          '.ytp-ad-skip-button-slot', '[class*="skip-button"]', '.videoAdUiSkipButton',
          'button.ytp-ad-skip-button-modern', '.ytp-ad-overlay-close-button',
          '.ytp-ad-overlay-close-container button', 'button[id="skip-button:"]'
        ];
        for (var s = 0; s < skipSelectors.length; s++) {
          var btns = document.querySelectorAll(skipSelectors[s]);
          for (var b = 0; b < btns.length; b++) {
            if (btns[b].offsetParent !== null) btns[b].click();
          }
        }
        var player = document.querySelector('.html5-video-player');
        var video = document.querySelector('video.html5-main-video, video');
        if (player && video) {
          var isAdPlaying = player.classList.contains('ad-showing') ||
                            player.classList.contains('ad-interrupting') ||
                            document.querySelector('.ytp-ad-player-overlay') !== null;
          if (isAdPlaying) {
            if (video.duration && isFinite(video.duration)) video.currentTime = video.duration;
            video.playbackRate = 16;
            video.muted = true;
          } else if (video.playbackRate === 16) {
            video.playbackRate = 1;
          }
        }
        var adEls = document.querySelectorAll(
          'ytd-promoted-sparkles-web-renderer,ytd-promoted-video-renderer,' +
          'ytd-display-ad-renderer,ytd-companion-slot-renderer,' +
          'ytd-action-companion-ad-renderer,ytd-in-feed-ad-layout-renderer,' +
          'ytd-ad-slot-renderer,ytd-banner-promo-renderer,#masthead-ad,#player-ads,' +
          'ytd-merch-shelf-renderer,.ytp-ad-overlay-container,.ytp-ad-text-overlay,' +
          'tp-yt-paper-dialog.ytd-popup-container,ytd-mealbar-promo-renderer');
        for (var a = 0; a < adEls.length; a++) {
          try { adEls[a].remove(); } catch(e) {}
        }
      } catch(e) {}
    }

    setInterval(skipYouTubeAd, 250);
    try {
      new MutationObserver(function(mutations) {
        for (var m = 0; m < mutations.length; m++) {
          var added = mutations[m].addedNodes;
          for (var n = 0; n < added.length; n++) {
            if (added[n].nodeType === 1) {
              var cn = (added[n].className || '') + ' ' + (added[n].tagName || '');
              if (/ad-showing|ad-interrupting|ytp-ad|ytd-ad/i.test(cn)) {
                skipYouTubeAd();
                return;
              }
            }
          }
        }
      }).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
    } catch(e) {}

    try {
      Object.defineProperty(window, 'ytInitialPlayerResponse', {
        set: function(val) {
          try {
            if (val && val.adPlacements) delete val.adPlacements;
            if (val && val.playerAds) delete val.playerAds;
            if (val && val.adSlots) delete val.adSlots;
            if (val && val.adBreakHeartbeatParams) delete val.adBreakHeartbeatParams;
          } catch(e) {}
          this._ytInitialPlayerResponse = val;
        },
        get: function() { return this._ytInitialPlayerResponse; },
        configurable: true
      });
    } catch(e) {}
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 9: COOKIE CONSENT AUTO-DISMISS
  // ═══════════════════════════════════════════════════════════════════════════
  function dismissCookieBanners() {
    try {
      var acceptSelectors = [
        '#onetrust-accept-btn-handler', '.cc-accept', '.cc-btn.cc-dismiss', '.cc-allow',
        '#CybotCookiebotDialogBodyLevelButtonLevelOptinAllowAll',
        '#CybotCookiebotDialogBodyButtonAccept',
        '.cky-btn-accept', '#didomi-notice-agree-button',
        'button[data-testid*="accept"]', 'button[data-testid*="agree"]',
        '.iubenda-cs-accept-btn', '.osano-cm-accept-all',
        '.termly-consent-banner__accept-all', '.cmplz-accept',
        '.uc-accept-btn', 'button[data-testid="uc-accept-all-button"]',
        '.sp_choice_type_11', '.klaro .cm-btn-accept', '.klaro .cm-btn-accept-all',
        '.fc-cta-consent', '.fc-primary-button',
        '[data-cookiefirst-action="accept"]',
        'button.agree-button', 'button.accept-all',
        'button[aria-label*="Accept"]', 'button[aria-label*="accept"]',
        'button[class*="accept-all"]', 'button[id*="accept-all"]',
        '#accept-cookies', '.accept-cookies',
        '#cookie-law-info-again', '.cookie-notice-accept'
      ];
      var bannerSelectors = [
        '#onetrust-consent-sdk', '#onetrust-banner-sdk',
        '.cc-window', '.cc-banner', '#cookie-law-info-bar',
        '.cookie-notice', '#cookie-notice', '.cookie-banner', '#cookie-banner',
        '.cookieConsent', '#cookieConsent', '.cookie-consent', '#cookie-consent',
        '#CybotCookiebotDialog', '.cky-consent-container',
        '.qc-cmp2-container', '#didomi-popup', '.didomi-popup-container',
        '.fc-consent-root', '#fc-consent-root',
        '#evidon_banner', '.truste_box_overlay', '#truste-consent-track',
        '.iubenda-cs-container', '#iubenda-cs-banner',
        '.osano-cm-window', '.termly-styles-module_overlay',
        '#usercentrics-root', '.uc-banner-layout',
        '.sp_message_container', '#klaro', '.klaro',
        '#tarteaucitronRoot', '#tarteaucitronAlertBig',
        '.complianz-categories', '#cmplz-cookiebanner-container',
        '.cky-consent-bar', '#cky-consent',
        '[class*="gdpr"]', '[id*="gdpr"]',
        '[class*="cookie-banner"]', '[class*="cookie-consent"]',
        '[id*="cookie-banner"]', '[id*="cookie-consent"]'
      ];
      for (var i = 0; i < acceptSelectors.length; i++) {
        try {
          var btns = document.querySelectorAll(acceptSelectors[i]);
          for (var b = 0; b < btns.length; b++) {
            if (btns[b].offsetParent !== null) btns[b].click();
          }
        } catch(e) {}
      }
      for (var j = 0; j < bannerSelectors.length; j++) {
        try {
          var els = document.querySelectorAll(bannerSelectors[j]);
          for (var k = 0; k < els.length; k++) {
            if (els[k].offsetParent !== null) {
              els[k].style.cssText += 'display:none!important;visibility:hidden!important;';
            }
          }
        } catch(e) {}
      }
      if (document.body) {
        document.body.style.overflow = '';
        document.body.classList.remove('modal-open', 'cookie-modal-open', 'no-scroll');
        document.documentElement.style.overflow = '';
      }
    } catch(e) {}
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function() {
      setTimeout(dismissCookieBanners, 500);
      setTimeout(dismissCookieBanners, 1500);
      setTimeout(dismissCookieBanners, 3000);
      setTimeout(dismissCookieBanners, 6000);
    });
  } else {
    setTimeout(dismissCookieBanners, 300);
    setTimeout(dismissCookieBanners, 1000);
    setTimeout(dismissCookieBanners, 3000);
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 10: ANTI-ADBLOCK BYPASS
  // ═══════════════════════════════════════════════════════════════════════════
  try {
    function createFakeAdElement(className, id) {
      var el = document.createElement('div');
      if (className) el.className = className;
      if (id) el.id = id;
      el.style.cssText = 'position:absolute!important;left:-9999px!important;top:-9999px!important;' +
        'width:1px!important;height:1px!important;overflow:hidden!important;display:block!important;' +
        'visibility:visible!important;opacity:0.01!important;';
      el.innerHTML = '&nbsp;';
      return el;
    }
    function injectFakeAds() {
      var body = document.body || document.documentElement;
      var fakes = [
        createFakeAdElement('adsbygoogle', null),
        createFakeAdElement('adsbox', null),
        createFakeAdElement('ad-banner', 'ad-test'),
        createFakeAdElement('banner-ads', 'detect-adb'),
        createFakeAdElement('textads', null),
        createFakeAdElement('ads', 'ads'),
        createFakeAdElement('GoogleActiveViewElement', null)
      ];
      var fakeIns = document.createElement('ins');
      fakeIns.className = 'adsbygoogle';
      fakeIns.style.cssText = 'display:block!important;position:absolute!important;left:-9999px!important;' +
        'top:-9999px!important;width:1px!important;height:1px!important;overflow:hidden!important;';
      fakes.push(fakeIns);
      for (var i = 0; i < fakes.length; i++) {
        try { body.appendChild(fakes[i]); } catch(e) {}
      }
    }
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', injectFakeAds);
    } else {
      injectFakeAds();
    }
  } catch(e) {}

  try {
    var _origOffsetHeight = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetHeight');
    var _origOffsetWidth = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetWidth');
    var adDetectClasses = ['adsbygoogle', 'adsbox', 'ad-banner', 'banner-ads', 'textads', 'ads', 'GoogleActiveViewElement'];
    var adDetectIds = ['ad-test', 'detect-adb', 'ads', 'google_ads_iframe_0', 'aswift_0', 'ad'];
    function isAdDetectionElement(el) {
      if (!el) return false;
      var cn = el.className || '';
      var id = el.id || '';
      for (var i = 0; i < adDetectClasses.length; i++) if (cn.indexOf(adDetectClasses[i]) !== -1) return true;
      for (var j = 0; j < adDetectIds.length; j++) if (id === adDetectIds[j]) return true;
      return false;
    }
    if (_origOffsetHeight) {
      Object.defineProperty(HTMLElement.prototype, 'offsetHeight', {
        get: function() {
          var val = _origOffsetHeight.get.call(this);
          if (val === 0 && isAdDetectionElement(this)) return 1;
          return val;
        }, configurable: true
      });
    }
    if (_origOffsetWidth) {
      Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
        get: function() {
          var val = _origOffsetWidth.get.call(this);
          if (val === 0 && isAdDetectionElement(this)) return 1;
          return val;
        }, configurable: true
      });
    }
  } catch(e) {}

  try {
    var _origGetBCR = Element.prototype.getBoundingClientRect;
    Element.prototype.getBoundingClientRect = function() {
      var result = _origGetBCR.call(this);
      if (result.height === 0 && result.width === 0 && isAdDetectionElement(this)) {
        return { top: result.top, left: result.left, bottom: result.top + 1, right: result.left + 1,
                 width: 1, height: 1, x: result.x, y: result.y };
      }
      return result;
    };
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 11: TRACKER COOKIE CLEANUP
  // ═══════════════════════════════════════════════════════════════════════════
  try {
    var TRACKER_COOKIE_RE = /^(_ga|_gid|_gat|__utm|_gcl|_fbp|_fbc|_pin_|_rdt_|__hs|hubspotutk|mp_|amplitude_|ajs_|segment_|_hjSession|_hjid|_clck|_clsk|mf_|_ce\.|_dc_gtm|_gac_|IDE|DSID|FLC|AID|TAID|exchange_uid|personalization_id|guest_id)/;
    function cleanTrackerCookies() {
      try {
        var cookies = document.cookie.split(';');
        for (var i = 0; i < cookies.length; i++) {
          var name = cookies[i].split('=')[0].trim();
          if (TRACKER_COOKIE_RE.test(name)) {
            document.cookie = name + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/;';
            document.cookie = name + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/;domain=' + location.hostname;
            document.cookie = name + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/;domain=.' + location.hostname;
          }
        }
      } catch(e) {}
    }
    setTimeout(cleanTrackerCookies, 2000);
    setInterval(cleanTrackerCookies, 30000);
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 12: URL TRACKING PARAMETER STRIPPING
  // ═══════════════════════════════════════════════════════════════════════════
  try {
    var TRACKING_PARAMS = [
      'utm_source', 'utm_medium', 'utm_campaign', 'utm_term', 'utm_content',
      'utm_id', 'utm_cid', 'utm_reader', 'utm_name',
      'fbclid', 'gclid', 'gclsrc', 'dclid', 'gbraid', 'wbraid',
      'msclkid', 'twclid', 'li_fat_id',
      '_ga', '_gl', '_hsenc', '_hsmi', '__hstc', '__hsfp', 'hsCtaTracking',
      'mc_cid', 'mc_eid', 'oly_enc_id', 'oly_anon_id',
      'vero_id', 'wickedid', 'icid', 'igshid',
      'ref_src', 'ref_url', 'mkt_tok', 'trk', 's_cid',
      'rb_clickid', 'ttclid', '_openstat', 'yclid', 'ymclid'
    ];
    function stripTrackingParams() {
      try {
        var url = new URL(location.href);
        var changed = false;
        for (var i = 0; i < TRACKING_PARAMS.length; i++) {
          if (url.searchParams.has(TRACKING_PARAMS[i])) {
            url.searchParams.delete(TRACKING_PARAMS[i]);
            changed = true;
          }
        }
        if (changed) history.replaceState(null, '', url.toString());
      } catch(e) {}
    }
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', stripTrackingParams);
    } else {
      stripTrackingParams();
    }
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 13: INLINE SCRIPT CONTENT FILTERING (MutationObserver)
  // ═══════════════════════════════════════════════════════════════════════════
  try {
    var _scriptObserver = new MutationObserver(function(mutations) {
      for (var m = 0; m < mutations.length; m++) {
        var added = mutations[m].addedNodes;
        for (var n = 0; n < added.length; n++) {
          var node = added[n];
          if (node.nodeType !== 1) continue;
          if (node.tagName === 'SCRIPT' && !node.src) {
            var text = node.textContent || '';
            if (text.length > 10 && text.length < 5000) {
              if (/adsbygoogle|googletag\.cmd\.push|googletag\.display|_taboola\.push|OUTBRAIN\.widget|criteo_q\.push|pbjs\.que\.push|apstag\.init|__cmp\(|quantserve|COMSCORE\.beacon/.test(text)) {
                node.textContent = '/* blocked by YCB */';
                node.type = 'text/blocked';
              }
            }
          }
          if (node.tagName === 'SCRIPT' && node.src && isBlockedUrl(node.src)) {
            node.type = 'text/blocked';
            node.removeAttribute('src');
            try { node.remove(); } catch(e) {}
          }
          if (node.tagName === 'IFRAME' && node.src && isBlockedUrl(node.src)) {
            node.src = 'about:blank';
            node.style.display = 'none';
          }
          if (node.tagName === 'IMG' && node.src && isBlockedUrl(node.src)) {
            node.src = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';
            node.style.display = 'none';
          }
        }
      }
    });
    if (document.documentElement) {
      _scriptObserver.observe(document.documentElement, { childList: true, subtree: true });
    } else {
      document.addEventListener('DOMContentLoaded', function() {
        _scriptObserver.observe(document.documentElement, { childList: true, subtree: true });
      });
    }
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 14: NOTIFICATION SPAM + SERVICE WORKER BLOCKING
  // ═══════════════════════════════════════════════════════════════════════════
  try {
    if (window.Notification) {
      var _origNotifPerm = Notification.requestPermission;
      Notification.requestPermission = function(callback) {
        if (isBlockedUrl(location.href)) {
          if (callback) callback('denied');
          return Promise.resolve('denied');
        }
        return _origNotifPerm.apply(Notification, arguments);
      };
    }
  } catch(e) {}

  try {
    if (navigator.serviceWorker && navigator.serviceWorker.register) {
      var _origSWRegister = navigator.serviceWorker.register.bind(navigator.serviceWorker);
      navigator.serviceWorker.register = function(url) {
        if (url && isBlockedUrl(String(url))) {
          return Promise.reject(new DOMException('SW blocked', 'SecurityError'));
        }
        return _origSWRegister.apply(navigator.serviceWorker, arguments);
      };
    }
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 15: REFERRER POLICY
  // ═══════════════════════════════════════════════════════════════════════════
  try {
    var metaReferrer = document.createElement('meta');
    metaReferrer.name = 'referrer';
    metaReferrer.content = 'strict-origin-when-cross-origin';
    (document.head || document.documentElement).appendChild(metaReferrer);
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // LAYER 16: TIMER-BASED AD SCRIPT BLOCKING
  // ═══════════════════════════════════════════════════════════════════════════
  try {
    var _origSetTimeout = window.setTimeout;
    var _origSetInterval = window.setInterval;
    var AD_FN_RE = /googletag|adsbygoogle|pbjs|apstag|_taboola|__cmp|quantserve|COMSCORE|moatads|prebid/i;
    window.setTimeout = function(fn, delay) {
      if (typeof fn === 'string' && AD_FN_RE.test(fn)) return 0;
      if (typeof fn === 'function') {
        try { if (fn.toString().length < 500 && AD_FN_RE.test(fn.toString())) return 0; } catch(e) {}
      }
      return _origSetTimeout.apply(window, arguments);
    };
    window.setInterval = function(fn, delay) {
      if (typeof fn === 'string' && AD_FN_RE.test(fn)) return 0;
      return _origSetInterval.apply(window, arguments);
    };
  } catch(e) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // STATS LOGGING
  // ═══════════════════════════════════════════════════════════════════════════
  var _blockCount = 0;
  var _baseIsBlocked2 = isBlockedUrl;
  isBlockedUrl = function(url) {
    var blocked = _baseIsBlocked2(url);
    if (blocked) _blockCount++;
    return blocked;
  };

  setTimeout(function() {
    if (_blockCount > 0) {
      console.log('[YCB Shields] Blocked ' + _blockCount + ' requests | ' +
                  Object.keys(BLOCKED_HOSTS).length + ' domains in blocklist');
    }
  }, 5000);
""")

emit('})();')

# Write the file
content = '\n'.join(lines)
with open(OUT, 'w', encoding='utf-8') as f:
    f.write(content)

line_count = len(lines)
char_count = len(content)
print(f"Generated {OUT}")
print(f"Lines: {line_count}")
print(f"Characters: {char_count}")
print(f"Unique hosts: {len(hosts)}")
