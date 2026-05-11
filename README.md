<div align="center">

# From Robotics to Game Feel
### Pilot Investigation of RL Locomotion as Perceived Emergent Behavior in Drone NPCs

*A controlled-isolation study of learned versus scripted locomotion in NPC drones.*

[![Course](https://img.shields.io/badge/Course-CS--GY%206943-7C3AED?style=flat-square)](#)
[![Semester](https://img.shields.io/badge/Semester-Spring%202026-blue?style=flat-square)](#)
[![Institution](https://img.shields.io/badge/NYU-Tandon-57068C?style=flat-square)](#)
[![Engine](https://img.shields.io/badge/Unity-2022.3.62f3-000000?style=flat-square&logo=unity)](#)
[![ML-Agents](https://img.shields.io/badge/ML--Agents-2.0.1-FF6B35?style=flat-square)](#)
[![Algorithm](https://img.shields.io/badge/Algorithm-PPO-1A1A1A?style=flat-square)](#)
[![Status](https://img.shields.io/badge/Status-Pilot%20Complete-2EA44F?style=flat-square)](#)
[![Paper](https://img.shields.io/badge/Paper-IEEEtran-FF4757?style=flat-square)](#)

**Kaylie Stuteville** · **Arthur Farwell Perry** · **Jessenth Ebenezer Sankar**
*Advised by Prof. Julian Togelius and Tim Merino*

</div>

---

## The Hook

In 2024, Embark Studios shipped *ARC Raiders*, a cooperative shooter whose NPC enemies generated widespread player commentary describing them as **"feeling smart"** or **"alive."** At GDC 2026, Embark engineer Martin Singh-Blom revealed why: it isn't a smarter AI brain. They isolated the **locomotion layer** entirely and trained it with physics-based machine learning. Because the enemies are robots, motion that reads as slightly mechanical *reinforces* believability rather than triggering uncanny valley.

That commercial precedent sets up a research question that has never been answered under controlled isolation:

> **Does learned locomotion produce a perceptibly different quality of emergent behavior than scripted locomotion?**

This project is the first attempt to answer it.

---

## The Idea in One Diagram

```
                  ┌──────────────────────────────────────┐
                  │   SHARED UNITY PHYSICS BODY          │
                  │   identical mass, hits, impulse      │
                  └──────────────────────────────────────┘
                                  ▲
                                  │  (the only variable)
                                  │
                        ┌─────────┴─────────┐
                        │                   │
                ┌───────▼────────┐  ┌───────▼────────┐
                │   PPO          │  │   PID          │
                │   (learned)    │  │   (scripted)   │
                └────────────────┘  └────────────────┘
```

Same body. Same hits. Same impulse. **Swap only the motor command generator.** Any preference signal that emerges is attributable to locomotion alone, not to physics configuration, navigation, or decision-making. That is the methodological contribution.

---

## The Four Controllers

Two PPO/PID pairs across two physics bodies. Within each pair, the only variable is learned vs. scripted.

<table>
<tr>
<td width="50%" valign="top">

### Pair 1 · Original physics body
**DRONE A · FRS-PPO** *(learned)*
Failure-Robust Stabilization. Four-stage failure curriculum: single → double → forced diagonal → combined dodge.

**DRONE B · FA-PID** *(scripted)*
Failure-Aware PID. Recomputes motor allocation after failures via a reduced control mixer. Graceful-descent fallback.

</td>
<td width="50%" valign="top">

### Pair 2 · Upgraded physics body
**DRONE D · DR-PPO** *(learned)*
Damage-Responsive. Fine-tuned from an intermediate hover checkpoint with single-motor failure active from step 0.

**DRONE C · HS-PID** *(scripted)*
Hover-Stabilized. Dynamic hover thrust, integrator reset on failure, wobble noise, and position leash.

</td>
</tr>
</table>

---

## Training Pipeline

**Unity 2022.3.62f3** · **ML-Agents 2.0.1** · **Proximal Policy Optimization**

### Stage 1 · Shared base checkpoint
A three-stage curriculum builds a robust hover foundation for both PPO branches.

| Stage | Skill | What it teaches |
|:-----:|:------|:----------------|
| 1 | **Stable hover** | Maintain altitude under no disturbances |
| 2 | **Domain randomization** | Randomize arm length, mass, per-motor thrust |
| 3 | **Disturbance robustness** | Wind disturbance and per-motor lag variation |

### Stage 2 · Fine-tuning for motor failure (two independent branches)

| Branch | Body | Curriculum | Steps | LR | Notes |
|:-------|:-----|:-----------|:-----:|:--:|:------|
| **DR-PPO** | Upgraded | Single-motor failure from step 0 | 10M | constant | Shorter time horizon |
| **FRS-PPO** | Original | Four-stage escalating failure | 8M | linear decay | Behavioral cloning from PID hover demos |

---

## The Playtest

<table>
<tr>
<td align="center" width="33%">

### N = 12
**Participants**
voluntary · anonymous
recruited by authors

</td>
<td align="center" width="33%">

### Watch-only
**Format**
pre-recorded video
~15 min · Google Form

</td>
<td align="center" width="33%">

### 2 × 2 paired
**Design**
two PPO/PID pairs
PPO order alternated

</td>
</tr>
</table>

Each drone appeared in the same fixed sequence: **stable hover → projectile damage from a turret → single-motor failure recovery.** A standardized framing screen directed attention to the motor-recovery moment. Watch-only follows Togelius (2013), which argues participatory assessment introduces interaction-bias distortion that third-person observation avoids.

**Instrument** (7-point Likert per drone, adapted from PXI and Guo et al. 2023):
`Competence` · `Intelligence` · `Believability` · `Naturalness` · `Aliveness` · `Motor-loss recovery` · `Discomfort` (reverse-coded) · plus forced-choice items and a discrimination task.

---

## Results

### Within-pair preference

PPO outscored PID on **every Likert measure** and **every forced-choice item**, in both pairs.

| Measure | Drone A FRS-PPO | Drone B FA-PID | Drone C HS-PID | Drone D DR-PPO |
|:--------|:---------------:|:--------------:|:--------------:|:--------------:|
| Competence | 5.42 (1.62) | 2.17 (1.34) | 6.17 (1.27) | **6.58 (0.51)** |
| Intelligence | 5.17 (1.47) | 1.92 (1.68) | 5.58 (1.51) | **6.58 (0.67)** |
| Believability | 5.00 (2.04) | 3.25 (2.09) | 5.67 (1.30) | **6.33 (0.78)** |
| Naturalness | 5.08 (1.78) | 2.92 (1.78) | 5.58 (1.16) | **6.08 (1.00)** |
| Aliveness | 5.50 (1.73) | 1.92 (1.08) | 5.17 (1.70) | **6.50 (0.52)** |
| Motor-loss recovery | 4.83 (1.53) | 2.00 (1.21) | 5.50 (1.31) | **6.25 (1.06)** |

*Mean (SD), N = 12, scale 1–7.*

### Forced choice (participants picking PPO over PID)

| Item | Pair 1 | Pair 2 |
|:-----|:------:|:------:|
| More capable | **11/12** | 7/12 |
| More believable | **11/12** | **9/12** |
| Felt more alive | **11/12** | 7/12 |

### The key finding: discrimination at chance

<div align="center">

|  Pair 1 |  Pair 2 |
|:-------:|:-------:|
| **6 / 12** | **6 / 12** |
| correctly identified PPO | correctly identified PPO |
| *(= chance)* | *(= chance)* |

*Mean self-reported confidence in those guesses: **3.3 / 5***

</div>

> **Players attribute intelligence to the locomotion without identifying its source.**

This is the qualitative outcome Arc Raiders commentary suggested would happen, now reproduced under controlled isolation.

---

## What the Signal Suggests

> **1. Learned locomotion is perceived as more alive.**
> Two independently fine-tuned PPO branches produced the same preference signal, mitigating single-implementation risk.

> **2. But it does not betray its learned origin.**
> Chance-level discrimination means players attribute intelligence to motion without recognizing it as machine-learned. This matches the qualitative pattern observed in *Arc Raiders* commentary.

> **3. Believability is not the same as competence.**
> A subset of participants criticized PPO drones for "staying too long in the air" before falling, perceiving the prolonged recovery as less believable than the rapid drop of the PID. A tension future stimulus design must address.

---

## Limitations (We Own Them)

We are not hiding what this pilot cannot claim.

- **Sample size.** N = 12 is enough to validate the stimulus and instrument and to show a directional signal. Not enough to estimate effect size or claim causation.
- **Asymmetric tuning.** Pair 2 controllers were rated higher overall. Notably, HS-PID (scripted) outscored FRS-PPO on competence and intelligence. A well-tuned PID can match a learned one.
- **Bullet knockback.** Physical destabilization from impacts drew attention to flight paths rather than motor recovery. Impulse was reduced 10× but the confound was not fully removed.
- **No counterbalancing.** Single-form deployment: every participant saw Pair 1 before Pair 2. PPO/PID display order was alternated within forms, but full counterbalancing across participants was absent.

These limitations directly motivate the methodology below.

---

## The Path Forward: A Five-Stage Methodology

Goal: **identify which kinematic properties drive the perception of emergent behavior.**

```
   ┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐
   │      1       │    │      2       │    │      3       │    │      4       │    │      5       │
   │   Policy     │ -> │    Vibes     │ -> │   Feature    │ -> │ Statistical  │ -> │ Confirmatory │
   │  population  │    │   triage     │    │  extraction  │    │  isolation   │    │    study     │
   └──────────────┘    └──────────────┘    └──────────────┘    └──────────────┘    └──────────────┘
```

1. **Policy population.** Train diverse PPO policies varying reward weights, seeds, curricula.
2. **Vibes triage.** Humans label 30–60s clips binary: "emergent" vs "robotic."
3. **Feature extraction.** Motor variance, recovery overshoot, asymmetric compensation, jitter spectrum.
4. **Statistical isolation.** Interpretable classifier links kinematic features to perception labels.
5. **Confirmatory study.** Synthetic clips vary candidate features in a larger sample (N ≥ 20).

> The N = 12 pilot produces the **labeled seed examples** needed to begin Step 1. Multi-controller pre-competition, motor-recovery curricula, and full NPC-stack integration follow.

---

## Three Contributions

<table>
<tr>
<td width="33%" align="center" valign="top">

### Methodology
**Shared physics isolation.**

Same body, same hits, same impulse. Swap only the controller. Locomotion becomes the single variable.

</td>
<td width="33%" align="center" valign="top">

### Empirical signal
**Directional preference with chance discrimination.**

PPO won every Likert and forced-choice measure across two pairs. Participants could not identify which controller was learned.

</td>
<td width="33%" align="center" valign="top">

### Scaling plan
**Five-stage perceptual triage.**

The pilot produces seed data. The methodology turns the directional signal into a causal kinematic feature map.

</td>
</tr>
</table>

---

## Tech Stack

<div align="center">

| Layer | Tools |
|:------|:------|
| **Engine** | Unity 2022.3.62f3 |
| **RL Framework** | Unity ML-Agents 2.0.1 |
| **Algorithm** | Proximal Policy Optimization (PPO) |
| **Observation Space** | 23-dimensional (position, orientation, velocities, motor states) |
| **Action Space** | 4-dimensional per-motor thrust |
| **Physics** | Unity Rigidbody, X-configuration quadrotor, slew-rate-limited motor dynamics |
| **Playtest Platform** | Google Forms (single deployment) |
| **Paper** | LaTeX (IEEEtran), 22 references |

</div>

---

## Repository Map

```
.
├── paper/
│   ├── main.tex                    # IEEEtran source, 7 pages
│   └── main.pdf                    # compiled paper
├── presentation/
│   ├── drone_rl_presentation.pptx  # 14-slide final deck
│   └── speaker_notes.md            # 30-45s per slide
├── playtest/
│   ├── create_playtest_form.gs     # Apps Script to generate the Google Form
│   └── playtest_form_setup_guide.md
├── unity/
│   ├── DroneBody.cs                # shared physics body
│   ├── IDroneController.cs         # controller interface
│   ├── FRS_PPO/                    # Pair 1 learned
│   ├── FA_PID/                     # Pair 1 scripted
│   ├── DR_PPO/                     # Pair 2 learned
│   └── HS_PID/                     # Pair 2 scripted
├── training/
│   ├── stage1_curriculum.yaml      # shared base
│   ├── dr_ppo_finetune.yaml
│   └── frs_ppo_finetune.yaml
└── qa_defense.md                   # 45-question defense prep
```

---

## Acknowledgments

**Prof. Julian Togelius** for guidance throughout the semester, the watch-only methodology framing, and *Assessing Believability*. **Tim Merino** for detailed weekly feedback on study scope, the multi-controller pre-competition design, and the vibes-triage seed. **Our twelve playtest participants** for their time and qualitative debriefs that turned numbers into a story.

This work was completed for **NYU Tandon CS-GY 6943 · AI for Games · Spring 2026**.

---

## Citation

```bibtex
@inproceedings{stuteville2026rl_locomotion,
  title     = {Pilot Investigation of {RL} Locomotion as
               Perceived Emergent Behavior in Drone {NPCs}},
  author    = {Stuteville, Kaylie and Perry, Arthur Farwell and
               Ebenezer Sankar, Jessenth},
  booktitle = {NYU Tandon CS-GY 6943 AI for Games, Final Project},
  year      = {2026},
  address   = {New York, NY, USA}
}
```

---

<div align="center">

*Movement alone can drive the perception of intelligence.*
*The locomotion layer deserves controlled study.*

**↑ this work is that study.**

</div>
