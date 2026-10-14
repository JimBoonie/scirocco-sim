# Online research findings: 1984 VW Scirocco II (53B), EU 1.8 GTI
Date 2026-10-13 PT. Read-only for the sim: no sim files were edited. Nothing below is invented. Where nothing was found, it says so.
Confidence: H = official or manufacturer data for this car; M = reputable secondary source, or a proxy close to this car; L = forum, single anecdote, or a weak proxy.

## 1. Vehicle-level data
| Item | Value | Applies to | Source | Conf | vs JSON |
|---|---|---|---|---|---|
| Wheelbase | 2400 mm | Scirocco II, all | https://www.volkswagen-newsroom.com/en/vehicle-data-scirocco-2-profile-19652 | H | agrees (S) |
| Track F/R | 1404 / 1372 mm | '85 EU 1.8 8V injection (GT/GTL/GTX brochure) | https://driversfound.com/scirocco/history/thescirocco/specs1.html | M-H | agrees (S) |
| Track F/R | 1390/1358 mm; "GTI and later models from 1984: 1404/1372" | Scirocco II | https://de.wikipedia.org/wiki/VW_Scirocco_II | M | agrees |
| Track F/R | 1389 / 1359 mm | '84 US 1.8 | driversfound (same page) | M | US car only |
| Unladen (kerb) weight | 920 kg | '85 EU 1.8 8V injection, 82 kW (DX) | driversfound (VW brochure data) | M-H | new; JSON has no curb weight |
| Kerb weight, other sources | 895 kg (GTI); 882 kg (GT II); 830-1000 kg range | various | ultimatespecs; cars-data.com; de.wikipedia | L-M | SD among sources |
| GVW | 1290 kg | '85 EU 1.8 | driversfound | M | new |
| Axle load **limits** F/R | 730 / 590 kg | '85 EU (all engines) | driversfound | M | these are permissible maximums, not static kerb axle loads |
| Static kerb axle loads | **NOT FOUND** | | | | |
| Ground clearance | 110 mm | '85 EU | driversfound | M | |
| Ground clearance at GVW | 121 mm | Mk1 Golf/Jetta/Scirocco (proxy) | https://workshop-manuals.com/volkswagen/golf-mk1/running_gear/technical_data/wheels_tyres/running_gear/ | M (proxy) | |
| Turning circle (kerb to kerb) | 10.5 m | '85 EU Scirocco II | driversfound | M | A1 manual gives 9.6 m (Golf Mk1); US '84 is 9.5 m |
| Ride height (wheel centre to arch, etc.) | **NOT FOUND** | | | | |

## 2. Brakes
| Item | Value | Applies to | Source | Conf |
|---|---|---|---|---|
| Front disc diameter | 239 mm vented | '85 EU 1.8 injection; '84 US 1.8 | driversfound; de.wikipedia (8V models) | M-H; agrees with JSON comment |
| Rear drum | 180 mm | same | driversfound | agrees with Bentley |
| 16V (not our car) | 256 mm F / 226 mm R discs | GTX 16V | de.wikipedia | for reference only |

## 3. Anti-roll bars
| Item | Value | Applies to | Source | Conf |
|---|---|---|---|---|
| F/R bars | 16 / 19 mm | **'84 US/Canada 1.8 column only** | driversfound | L-M for the EU GTI |
| Front bar | 20 mm | Mk2 Golf GTI 1985-87 / 90-92 (bush kit SPF1913-20K) | https://shop.sohiperformance.com/product/superpro-1985-volkswagen-golf-gti-front-20mm-sway-bar-mount-bushing-kit-spf1913-20k | L (different platform) |
| Front bar | "stock GTI 17mm front swaybar" (forum) | Mk1 GTI (proxy, unspecified) | http://www.scirocco.org/list/archives/2000/January/msg00812.html | L |
| Rear bar | 24 mm (owner claim; disputed in the same thread) | Scirocco 16V | https://www.vwvortex.com/threads/no-poly-bushings-for-24mm-rear-bar.8495265/ | L |
**Conflict flag:** the JSON tags 16/19 mm as "S? (driversfound)", but on that page the 16/19 mm figure appears only in the '84 US column. The EU column is blank. No EU 1.8 GTI bar diameter was found, so the 16/19 mm value should be treated as SD/E for the EU car.

## 4. Steering
| Item | Value | Applies to | Source | Conf |
|---|---|---|---|---|
| Overall ratio 17.32:1, 3.5 turns, lock 40°22' inner / 32°55' outer | Mk1 Golf/Jetta/Scirocco A1 manual | workshop-manuals.com (above) | M (proxy); same as existing S |
| 3.9 turns lock to lock, "turning ratio 20.8:1" | Cabriolet (A1, US) | https://cabby-info.com/suspension.htm | L-M; SD with 3.5 |
| About 3.85 turns (Mk1 Scirocco manual rack); 3.25 (87 16V power rack) | owner counts | https://www.vwvortex.com/threads/steering-ratio-how-many-turns-lock-to-lock.3130814/ | L |
| 4.1 turns manual / 3.2 power | Mk2 Golf/Jetta (owner measurement) | https://www.specialstage.com/threads/p-s-or-quicker-steering-on-vw-scirocco.7292/ | L (proxy) |
| Rack length about 20.63 in (524 mm) per a Cardone reman listing; inner joints about 22 in (559 mm) apart (owner estimate) | Mk1 rack (the 22 in figure is a PS rack) | https://grassrootsmotorsports.com/forum/grm/mk1-vw-steering-rack-measurement/180479/page1/ | L |
| Rack travel (mm) | **NOT FOUND** | | | |
Note: the existing SD flag stands. The turns-lock-to-lock figures cluster at 3.5 (A1 manual) vs about 3.85-3.9 (owner counts and the US Cabriolet).

## 5. Springs
| Item | Value | Applies to | Source | Conf |
|---|---|---|---|---|
| Front spring, VW OE 171 411 105 G = Lesjöfors 4095004 | fitment list covers Scirocco 53B 1.3/1.5/1.6 (not listed for 1.8 GTI) | https://www.rexbo.co.uk/lesjofors/coil-spring-4095004 | M for part number; proxy for the GTI |
| Front aftermarket (KYB RG1262 / Maxgear 60-0992D / Japko) | free length **347 mm**, OD 135 mm, wire 11.5 mm, 1.89 kg | listed for Scirocco 53B 1.8 112 PS 08/82-12/89 and 1.6 EW | http://www.ucando.pl/k/sprezyny-zawieszenia-vw-scirocco-53b-18-112-km-82-kw/U0302-121-214-1750 ; https://2407.pl/en/coil-springs/volkswagen-cars/scirocco-53b-14065/modif-61691/ | M (aftermarket, OE-equivalent) |
| Front alternative (Meyle 100 041 0002) | 356 mm, wire 11.3 mm | same vehicles | same | M; SD with 347 |
| Front Sachs 997 436 | OD 138 mm, wire 11.25 mm | 1.8 112 PS listing | ucando | M |
| Rear (KYB RA5071 / Maxgear 60-1047 / JP 8161000116) | free length **348 mm**, OD 98 mm, wire 10 mm, 1.88 kg | 1.8 112 PS and 1.6 | ucando; 2407.pl | M |
| Rear Lesjöfors 4295015 / 4295030; Kilen 466020 ("for vehicles with spoiler") | dimensions not shown | 1.8 112 PS | ucando | listing only |
| OE spring **rates**, Mk1 Golf GTI (CCC magazine, Dec 1994, quoted on a forum) | front 85 lb/in linear = **14.9 N/mm**; rear progressive 52/58/65 lb/in = **9.1/10.2/11.4 N/mm** | Mk1 Golf (GTI implied) **proxy** | https://www.clubgti.com/forums/index.php?threads%2Fmk1-oem-and-eibach-spring-rates.221763%2F= | L-M (proxy, secondary) |
| Scirocco II-specific OE spring rates | **NOT FOUND** (Eibach Pro-Kit E8501-140 gives axle-load ratings of 785/630 kg only, no rates) | | | |

## 6. Dampers
Force/velocity data, or OE Boge/Sachs valving for the 53B: **NOT FOUND.** No public dyno curves turned up. The existing rear-damper lengths (TRW/Monroe/Bilstein) remain the best data.

## 7. Bushing stiffness, hardpoint coordinates, body dimension drawings
**NOT FOUND** in public sources this session: no VW body repair manual measuring points for A1/53B and no bushing rate data. The E values stand.

## 8. Replaceable E values (suggested only; nothing was edited)
| JSON key | Old (tag) | Candidate | Basis |
|---|---|---|---|
| front_macpherson.spring_free_length_m | 0.37 (E) | 0.347 (SD 0.356) | aftermarket OE-equivalent listings for the 53B 1.8 112 PS |
| rear spring free length (not in JSON) | - | 0.348 m, OD 98, wire 10 mm | same |
| rear_twist_beam.spring_rate_N_per_mm | 14.0 (E) | 9.1 to 11.4 progressive (Mk1 Golf proxy, L-M) | CCC 1994. **Much softer than the E value** |
| front spring_rate_N_per_mm | 17.2 (SD) | 14.9 (Mk1 Golf proxy) | extends the existing SD range (77-98 lb/in) |
| curb weight / GVW (new) | - | 920 kg / 1290 kg; axle limits 730/590 kg | driversfound '85 EU 1.8 inj |
| brake disc (new) | - | 239 mm vented front, 180 mm rear drum | driversfound / wiki |

## 9. Conflicts with existing S values
1. ARB 16/19 mm "S?": the source column is the '84 US car, not the EU GTI. Downgrade to SD/E.
2. Turns lock to lock: 3.5 (A1 manual) vs about 3.85-3.9 (US Cabriolet / owners). The SD flag already exists; the new data reinforces it.
3. Front spring: the 14.9 N/mm proxy is lower than the 17.2 SD value.
4. Turning circle: 10.5 m (EU Scirocco II brochure) vs 9.6 m (A1 manual). This is relevant if the 13in-wheel A1 lock angles are reused for 14in wheels.
