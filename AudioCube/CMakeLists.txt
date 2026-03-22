cmake_minimum_required(VERSION 3.15)

project(AudioCube VERSION 0.0.1)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_subdirectory(JUCE) 

juce_add_gui_app(AudioCube
    COMPANY_NAME "EthanChen"
    PRODUCT_NAME "AudioCube")

target_sources(AudioCube PRIVATE 
    Source/main_audio_processor.cpp
    Source/test_sampler.h
    Source/test_sampler.cpp
    Source/grid_component.h
    Source/grid_component.cpp
    Source/music_model.h
    Source/music_model.cpp
    Source/sequencer.h
    Source/sequencer.cpp
)

target_include_directories(AudioCube PRIVATE Source)

target_link_libraries(AudioCube PRIVATE
    juce::juce_audio_utils
    juce::juce_audio_devices
    juce::juce_audio_formats
    juce::juce_graphics
    juce::juce_gui_basics)