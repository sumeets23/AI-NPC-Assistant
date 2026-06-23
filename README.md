# AI NPC Framework for Unity

# AI NPC Framework for Unity

<img width="3037" height="1695" alt="Screenshot 2026-06-23 160727" src="https://github.com/user-attachments/assets/c5c6f33c-e853-414c-b2c3-e2f8ca2296ea" />

A modular Unity framework for building AI-powered NPCs that can listen, think, and speak in real time.

## Overview

This project demonstrates a conversational NPC pipeline built with Unity 2022.3.62f3. It integrates voice input, language model responses, text-to-speech, lip sync, and animation control to create an interactive character experience.

The repository includes:
- single NPC conversation flow,
- multi-NPC dialogue demo,
- voice-based transcription support,
- local and cloud AI provider support,
- avatar animation and lip sync integration,
- and optional vision/image workflows.

## Core Features

- Real-time speech-to-text input
- OpenAI chat completion support
- Local LLM endpoint support
- ElevenLabs text-to-speech
- Windows local TTS fallback
- Oculus Lip Sync support
- Animated listening/thinking/speaking states
- NPC-to-NPC conversation mode
- Keyword-based event triggers
- Optional vision and image generation module

## Tech Stack

- Unity 2022.3.62f3
- HDRP
- Meta XR SDK
- XR Interaction Toolkit
- Meta Voice Dictation SDK
- Whisper
- OpenAI Unity SDK
- ElevenLabs SDK
- Oculus Lip Sync
- TextMeshPro

## Main Modules

- `NPCController` orchestrates the conversation flow
- `ListenerModule` handles microphone input and transcription
- `ThinkerModule` sends prompts to the LLM provider
- `SpeakerModule` turns responses into audio
- `AudioManager` queues and plays speech clips
- `MotionControllerModule` controls avatar animations
- `NPCConvoManager` manages NPC-to-NPC dialogue
- `VisionModule` handles camera sampling and image-based prompts

## How It Works

1. The NPC listens for player speech.
2. The listener transcribes the audio into text.
3. The text is sent to the selected LLM provider.
4. The generated response is converted into speech.
5. The audio is queued and played through the NPC.
6. The avatar switches between listening, thinking, and speaking animations.
7. Dictation is disabled while the NPC speaks to prevent feedback loops.

## Setup

1. Open the project in Unity 2022.3.62f3.
2. Configure the OpenAI and ElevenLabs credentials.
3. Assign the NPC prefab references in the inspector.
4. Set the NPC personality in `ThinkerModule`.
5. Choose a TTS voice in `SpeakerModule`.
6. Verify avatar blendshapes and lip sync settings.
7. Enter Play Mode and test the demo scene.

## Demos

- NPC Demo: one-on-one player conversation


## Notes

- The project supports both hosted and local AI workflows.
- Audio playback is queued to avoid overlapping speech.
- Whisper mode uses microphone thresholds and silence detection to reduce noise triggers.
- Multi-NPC mode requires separate animators and proper collider/tag configuration.

## License

MIT License

