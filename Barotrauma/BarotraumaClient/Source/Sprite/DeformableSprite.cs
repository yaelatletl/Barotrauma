﻿using Barotrauma.SpriteDeformations;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class DeformableSprite
    {
        private static List<DeformableSprite> list = new List<DeformableSprite>();

        private int triangleCount;

        private VertexBuffer vertexBuffer, flippedVertexBuffer;
        private IndexBuffer indexBuffer;

        private Vector2 uvTopLeft, uvBottomRight;
        private Vector2 uvTopLeftFlipped, uvBottomRightFlipped;

        private Vector2[] deformAmount;
        private int deformArrayWidth, deformArrayHeight;

        private int subDivX, subDivY;
        
        private static Effect effect;

        private Point spritePos;
        private Point spriteSize;

        partial void InitProjSpecific(XElement element, int? subdivisionsX, int? subdivisionsY)
        {
            if (effect == null)
            {
#if WINDOWS
                effect = GameMain.Instance.Content.Load<Effect>("Effects/deformshader");
#endif
#if LINUX || OSX                
                effect = GameMain.Instance.Content.Load<Effect>("Effects/deformshader_opengl");
#endif
            }
            
            //use subdivisions configured in the xml if the arguments passed to the method are null
            Vector2 subdivisionsInXml = element.GetAttributeVector2("subdivisions", Vector2.One);
            subDivX = subdivisionsX ?? (int)subdivisionsInXml.X;
            subDivY = subdivisionsY ?? (int)subdivisionsInXml.Y;

            if (subDivX <= 0 || subDivY <= 0)
            {
                throw new ArgumentException("Deformable sprites must have one or more subdivisions on each axis.");
            }

            foreach (DeformableSprite existing in list)
            {
                //share vertex and index buffers if there's already 
                //an existing sprite with the same texture and subdivisions
                if (existing.sprite.Texture == sprite.Texture && 
                    existing.subDivX == subDivX && 
                    existing.subDivY == subDivY && 
                    existing.Sprite.SourceRect == Sprite.SourceRect)
                {
                    vertexBuffer = existing.vertexBuffer;
                    flippedVertexBuffer = existing.flippedVertexBuffer;
                    indexBuffer = existing.indexBuffer;
                    triangleCount = existing.triangleCount;
                    uvTopLeft = existing.uvTopLeft;
                    uvBottomRight = existing.uvBottomRight;
                    uvTopLeftFlipped = existing.uvTopLeftFlipped;
                    uvBottomRightFlipped = existing.uvBottomRightFlipped;
                    
                    Deform(new Vector2[,]
                    {
                        { Vector2.Zero, Vector2.Zero },
                        { Vector2.Zero, Vector2.Zero }
                    });
                    return;
                }
            }

            if (sprite.Texture != null)
            {
                SetupVertexBuffers();
                SetupIndexBuffer();
            }
            list.Add(this);
        }

        private void SetupVertexBuffers()
        {
            Vector2 textureSize = new Vector2(sprite.Texture.Width, sprite.Texture.Height);
            var pos = sprite.SourceRect.Location;
            var size = sprite.SourceRect.Size;

            uvTopLeft = Vector2.Divide(pos.ToVector2(), textureSize);
            uvBottomRight = Vector2.Divide((pos + size).ToVector2(), textureSize);
            uvTopLeftFlipped = Vector2.Divide(new Vector2(pos.X + size.X, pos.Y), textureSize);
            uvBottomRightFlipped = Vector2.Divide(new Vector2(pos.X, pos.Y + size.Y), textureSize);

            for (int i = 0; i < 2; i++)
            {
                bool flip = i == 1;
            
                var vertices = new VertexPositionColorTexture[(subDivX + 1) * (subDivY + 1)];
                for (int x = 0; x <= subDivX; x++)
                {
                    for (int y = 0; y <= subDivY; y++)
                    {
                        //{0,0} -> {1,1}
                        Vector2 relativePos = new Vector2(x / (float)subDivX, y / (float)subDivY);

                        Vector2 uvCoord = flip ?
                            uvTopLeftFlipped + (uvBottomRightFlipped - uvTopLeftFlipped) * relativePos :
                            uvTopLeft + (uvBottomRight - uvTopLeft) * relativePos;

                        vertices[x + y * (subDivX + 1)] = new VertexPositionColorTexture(
                            position: new Vector3(relativePos.X * sprite.SourceRect.Width, relativePos.Y * sprite.SourceRect.Height, 0.0f),
                            color: Color.White,
                            textureCoordinate: uvCoord);
                    }
                }

                if (flip)
                {
                    if (flippedVertexBuffer != null && flippedVertexBuffer.VertexCount != vertices.Length)
                    {
                        flippedVertexBuffer.Dispose();
                        flippedVertexBuffer = null;
                    }
                    if (flippedVertexBuffer == null)
                    {
                        flippedVertexBuffer = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.None);
                    }
                    flippedVertexBuffer.SetData(vertices);
                }
                else
                {
                    if (vertexBuffer != null && vertexBuffer.VertexCount != vertices.Length)
                    {
                        vertexBuffer.Dispose();
                        vertexBuffer = null;
                    }
                    if (vertexBuffer == null)
                    {
                        vertexBuffer = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.None);
                    }
                    vertexBuffer.SetData(vertices);
                }
            }
            
            spritePos = sprite.SourceRect.Location;
            spriteSize = sprite.SourceRect.Size;
        }

        private void SetupIndexBuffer()
        {
            triangleCount = subDivX * subDivY * 2;
            var indices = new ushort[triangleCount * 3];
            int offset = 0;
            for (int i = 0; i < triangleCount / 2; i++)
            {
                indices[i * 6] = (ushort)(i + offset + 1);
                indices[i * 6 + 1] = (ushort)(i + offset + (subDivX + 1) + 1);
                indices[i * 6 + 2] = (ushort)(i + offset + (subDivX + 1));

                indices[i * 6 + 3] = (ushort)(i + offset);
                indices[i * 6 + 4] = (ushort)(i + offset + 1);
                indices[i * 6 + 5] = (ushort)(i + offset + (subDivX + 1));

                if ((i + 1) % subDivX == 0) offset++;
            }

            indexBuffer?.Dispose();
            indexBuffer = null;

            indexBuffer = new IndexBuffer(GameMain.Instance.GraphicsDevice, IndexElementSize.SixteenBits, indices.Length, BufferUsage.None);
            indexBuffer.SetData(indices);

            Deform(new Vector2[,]
            {
                { Vector2.Zero, Vector2.Zero },
                { Vector2.Zero, Vector2.Zero }
            });
        }

        /// <summary>
        /// Deform the vertices of the sprite using an arbitrary function. The in-parameter of the function is the
        /// normalized position of the vertex (i.e. 0,0 = top-left corner of the sprite, 1,1 = bottom-right) and the output
        /// is the amount of deformation.
        /// </summary>
        public void Deform(Func<Vector2, Vector2> deformFunction)
        {
            var deformAmount = new Vector2[subDivX + 1, subDivY + 1];
            for (int x = 0; x <= subDivX; x++)
            {
                for (int y = 0; y <= subDivY; y++)
                {
                    deformAmount[x, y] = deformFunction(new Vector2(x / (float)subDivX, y / (float)subDivY));
                }
            }
            Deform(deformAmount);
        }

        public void Deform(Vector2[,] deform)
        {
            deformArrayWidth = deform.GetLength(0);
            deformArrayHeight = deform.GetLength(1);
            if (deformAmount == null || deformAmount.Length != deformArrayWidth * deformArrayHeight)
            {
                deformAmount = new Vector2[deformArrayWidth * deformArrayHeight];
            }
            for (int x = 0; x < deformArrayWidth; x++)
            {
                for (int y = 0; y < deformArrayHeight; y++)
                {
                    deformAmount[x + y * deformArrayWidth] = deform[x, y];
                }
            }
        }

        public void Reset()
        {
            Deform(new Vector2[,]
            {
                { Vector2.Zero, Vector2.Zero },
                { Vector2.Zero, Vector2.Zero }
            });
        }

        public Matrix GetTransform(Vector3 pos, Vector2 origin, float rotate, Vector2 scale)
        {
            return  
                Matrix.CreateTranslation(-origin.X, -origin.Y, 0) *
                Matrix.CreateScale(scale.X, -scale.Y, 1.0f) *
                Matrix.CreateRotationZ(-rotate) *
                Matrix.CreateTranslation(pos);
        }

        public void Draw(Camera cam, Vector3 pos, Vector2 origin, float rotate, Vector2 scale, Color color, bool flip = false)
        {
            if (sprite.Texture == null) { return; }

            // If the source rect is modified, we should recalculate the vertex buffer.
            if (sprite.SourceRect.Location != spritePos || sprite.SourceRect.Size != spriteSize)
            {
                SetupVertexBuffers();
            }

#if LINUX
            effect.Parameters["TextureSampler+xTexture"].SetValue(sprite.Texture);
#else
            effect.Parameters["xTexture"].SetValue(sprite.Texture);
#endif

            Matrix matrix = GetTransform(pos, origin, rotate, scale);
            effect.Parameters["xTransform"].SetValue(matrix * cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f);
            effect.Parameters["tintColor"].SetValue(color.ToVector4());
            effect.Parameters["deformArray"].SetValue(deformAmount);
            effect.Parameters["deformArrayWidth"].SetValue(deformArrayWidth);
            effect.Parameters["deformArrayHeight"].SetValue(deformArrayHeight);
            effect.Parameters["uvTopLeft"].SetValue(flip ? uvTopLeftFlipped : uvTopLeft);
            effect.Parameters["uvBottomRight"].SetValue(flip ? uvBottomRightFlipped : uvBottomRight);
            effect.GraphicsDevice.SetVertexBuffer(flip ? flippedVertexBuffer : vertexBuffer);
            effect.GraphicsDevice.Indices = indexBuffer;
            effect.CurrentTechnique.Passes[0].Apply();
            effect.GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, triangleCount);
        }

        public void Remove()
        {
            sprite?.Remove();
            sprite = null;

            list.Remove(this);

            foreach (DeformableSprite otherSprite in list)
            {
                //another sprite is using the same vertex buffer, cannot dispose it yet
                if (otherSprite.vertexBuffer == vertexBuffer) return;
            }

            vertexBuffer?.Dispose();
            vertexBuffer = null;
            flippedVertexBuffer?.Dispose();
            flippedVertexBuffer = null;
            indexBuffer?.Dispose();
            indexBuffer = null;
        }
        
        #region Editing

        public GUIComponent CreateEditor(GUIComponent parent, List<SpriteDeformation> deformations, string parentDebugName)
        {
            var container = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), parent.RectTransform))
            {
                AbsoluteSpacing = 5
            };

            new GUITextBlock(new RectTransform(new Point(container.Rect.Width, 30), container.RectTransform), "Sprite Deformations");

            var resolutionField = GUI.CreatePointField(new Point(subDivX + 1, subDivY + 1), 26, "Resolution", container.RectTransform, 
                "How many vertices the deformable sprite has on the x and y axes. Larger values make the deformations look smoother, but are more performance intensive.");
            GUINumberInput xField = null, yField = null;
            foreach (GUIComponent child in resolutionField.GetAllChildren())
            {
                if (yField == null)
                {
                    yField = child as GUINumberInput;
                }
                else
                {
                    xField = child as GUINumberInput;
                    if (xField != null) break;
                }
            }
            xField.MinValueInt = 2;
            xField.MaxValueInt = SpriteDeformationParams.ShaderMaxResolution.X - 1;
            xField.OnValueChanged = (numberInput) => { ChangeResolution(); };
            yField.MinValueInt = 2;
            yField.MaxValueInt = SpriteDeformationParams.ShaderMaxResolution.Y - 1;
            yField.OnValueChanged = (numberInput) => { ChangeResolution(); };

            void ChangeResolution()
            {
                subDivX = xField.IntValue - 1;
                subDivY = yField.IntValue - 1;

                foreach (SpriteDeformation deformation in deformations)
                {
                    deformation.SetResolution(new Point(xField.IntValue, yField.IntValue));
                }
                SetupVertexBuffers();
                SetupIndexBuffer();
            }

            foreach (SpriteDeformation deformation in deformations)
            {
                var deformEditor = new SerializableEntityEditor(container.RectTransform, deformation.DeformationParams, false, true);
                deformEditor.RectTransform.MinSize = new Point(deformEditor.Rect.Width, deformEditor.Rect.Height);
            }

            var deformationDD = new GUIDropDown(new RectTransform(new Point(container.Rect.Width, 30), container.RectTransform), "Add new sprite deformation");
            deformationDD.OnSelected = (selected, userdata) =>
            {
                deformations.Add(SpriteDeformation.Load((string)userdata, parentDebugName));
                deformationDD.Text = "Add new sprite deformation";
                return false;
            };

            foreach (string deformationType in SpriteDeformation.DeformationTypes)
            {
                deformationDD.AddItem(deformationType, deformationType);
            }

            container.RectTransform.Resize(new Point(
                container.Rect.Width, container.Children.Sum(c => c.Rect.Height + container.AbsoluteSpacing)), false);
            container.RectTransform.MaxSize = new Point(int.MaxValue, container.Rect.Height);
            container.Recalculate();

            return container;
        }

        #endregion
    }
}
