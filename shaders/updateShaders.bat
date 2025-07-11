@echo off
setlocal
echo Compiling shaders...

echo Compiling fragment to SPIR-V
glslangvalidator -V shaders/fragment.frag -o shaders/fragment.spv
echo Compiling vertex to SPIR-V
glslangvalidator -V shaders/vertex.vert -o shaders/vertex.spv

echo Compiling empty shaders as a safety step...
glslangvalidator -V shaders/empty.frag -o shaders/empty.frag.spv
glslangvalidator -V shaders/empty.vert -o shaders/empty.vert.spv

echo Compiling skeletal shaders now...

echo Starting with Vertex shader...
glslangvalidator -V shaders/skeletal_vertex.vert -o shaders/skeletal_vertex.spv
echo And now the Fragment shader...
glslangvalidator -V shaders/skeletal_fragmet.frag -o shaders/skeletal_fragment.spv