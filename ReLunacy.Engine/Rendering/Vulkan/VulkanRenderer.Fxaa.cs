using System.Numerics;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

public sealed unsafe partial class VulkanRenderer
{
    private VkRenderPass _rpFxaa;
    private VkPipeline _fxaaPipeline;

    private const string FxaaFragmentGlsl = """
        #version 450
        layout(set = 0, binding = 0) uniform sampler2D scene;
        layout(push_constant) uniform Parameters { vec2 inverseSize; } p;
        layout(location = 0) out vec4 outputColor;

        float luma(vec3 rgb) {
            // Use perceptual contrast for edge detection and keep the HDR values for blending.
            return sqrt(dot(clamp(rgb, 0.0, 1.0), vec3(0.299, 0.587, 0.114)));
        }

        void main() {
            vec2 pixel = p.inverseSize;
            vec2 uv = gl_FragCoord.xy * pixel;
            vec4 center = texelFetch(scene, ivec2(gl_FragCoord.xy), 0);
            float middle = luma(center.rgb);
            float nw = luma(texture(scene, uv + vec2(-1.0, -1.0) * pixel).rgb);
            float ne = luma(texture(scene, uv + vec2( 1.0, -1.0) * pixel).rgb);
            float sw = luma(texture(scene, uv + vec2(-1.0,  1.0) * pixel).rgb);
            float se = luma(texture(scene, uv + vec2( 1.0,  1.0) * pixel).rgb);
            float darkest = min(middle, min(min(nw, ne), min(sw, se)));
            float brightest = max(middle, max(max(nw, ne), max(sw, se)));
            if (brightest - darkest < max(0.0312, brightest * 0.125)) {
                outputColor = center;
                return;
            }

            // Estimate the edge direction from diagonal samples and blend along it.
            vec2 direction = vec2(sw + se - nw - ne, nw + sw - ne - se);
            float reduce = max((nw + ne + sw + se) * (0.25 * 0.125), 1.0 / 128.0);
            direction = clamp(direction / (min(abs(direction.x), abs(direction.y)) + reduce), -8.0, 8.0) * pixel;
            vec3 nearBlend = 0.5 * (texture(scene, uv - direction / 6.0).rgb
                                  + texture(scene, uv + direction / 6.0).rgb);
            vec3 farBlend = 0.5 * nearBlend + 0.25 * (texture(scene, uv - direction * 0.5).rgb
                                                   + texture(scene, uv + direction * 0.5).rgb);
            float blendedLuma = luma(farBlend);
            outputColor = vec4(blendedLuma < darkest || blendedLuma > brightest ? nearBlend : farBlend, center.a);
        }
        """;

    private void RecordFxaa()
    {
        if (_fxaaPipeline.Handle == 0)
            _fxaaPipeline = CreateFullscreenPipeline(FxaaFragmentGlsl, _rpFxaa, additive: false);

        // Scene-colour materials have finished sampling the opaque snapshot. Reuse that image
        // for the final composite so the fullscreen pass can write back to the exposed viewport texture.
        CopySceneColor(VkImageLayout.ShaderReadOnlyOptimal);
        RecordFullscreenPass(_fxaaPipeline, _rpFxaa, _fbPostProcess[_slot], _width, _height,
            GetFullscreenSet(_sceneColorView[_slot]), new Vector4(1f / _width, 1f / _height, 0f, 0f), default);
    }
}
