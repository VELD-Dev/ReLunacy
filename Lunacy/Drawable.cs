namespace Lunacy
{
	//Buffers are split up due to how not all vertex attributes are currently known.
	//Creating one single buffer structure where the data is interweaved could lead to issues with excess memory usage for meshes where those extra vertex attributes aren't in use.
	//One example of such is blending, only some MobyObj meshes have this and it would be a waste of memory to store this for ties, zone meshes, and shrubs.
	public class Drawable
	{
		public List<Transform> transforms = new List<Transform>();
		int VwBO;
		int VpBO;
		int VtcBO;
		int VAO;
		int EBO;
		int indexCount;
		public Material material { get; private set; }

		public Drawable()
		{
			Prepare();
		}
		public Drawable(CMoby moby, CMoby.MobyMesh mesh)
		{
			Prepare();
			moby.GetBuffers(mesh, out uint[] indices, out float[] vPositions, out float[] vTexCoords);
			SetVertexPositions(vPositions);
			SetVertexTexCoords(vTexCoords);
			SetIndices(indices);
			SetMaterial(new Material(moby.shaderDB[mesh.shaderIndex]));
		}
		public Drawable(CTie tie, CTie.TieMesh mesh)
		{
			Prepare();

			tie.GetBuffers(mesh, out uint[] indices, out float[] vPositions, out float[] vTexCoords);

			SetVertexPositions(vPositions);
			SetVertexTexCoords(vTexCoords);
			SetIndices(indices);
			SetMaterial(new Material(mesh.shader));
		}
		public Drawable(ref CZone.UFrag mesh)
		{
			Prepare();
			SetVertexPositions(mesh.GetVertPositions());
			SetVertexTexCoords(mesh.GetUVs());
			SetIndices(mesh.GetIndices());
            //Texture? tex = (mesh.shader.albedo == null ? null : new Texture(mesh.shader.albedo));
            SetMaterial(new Material(mesh.GetShader()));
        }

		public void Prepare()
		{
			VAO = GL.GenVertexArray();
			EBO = GL.GenBuffer();
		}

		public void SetIndices(uint[] indices)
		{
			GL.BindVertexArray(VAO);
			GL.BindBuffer(BufferTarget.ElementArrayBuffer, EBO);
			GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);
			indexCount = indices.Length;
		}

		//It goes:
		//	0: vertex positions  (3 floats)
		//	1: vertex tex coords (2 floats)
		public void SetVertexPositions(float[] vpositions)
		{
			VpBO = GL.GenBuffer();
			GL.BindBuffer(BufferTarget.ArrayBuffer, VpBO);
			GL.BufferData(BufferTarget.ArrayBuffer, vpositions.Length * sizeof(float), vpositions, BufferUsageHint.StaticDraw);

			GL.BindVertexArray(VAO);
			GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
			GL.EnableVertexAttribArray(0);
		}
		public void SetVertexTexCoords(float[] vtexcoords)
		{
			VtcBO = GL.GenBuffer();
			GL.BindBuffer(BufferTarget.ArrayBuffer, VtcBO);
			GL.BufferData(BufferTarget.ArrayBuffer, vtexcoords.Length * sizeof(float), vtexcoords, BufferUsageHint.StaticDraw);

			GL.BindVertexArray(VAO);
			GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
			GL.EnableVertexAttribArray(1);
		}
		public void SetMaterial(Material mat)
		{
			material = mat;
		}

		public void AddDrawCall(Transform transform)
		{
			transforms.Add(transform);
		}

		public void ConsolidateDrawCalls()
		{
			Matrix4[] transformMatrices = new Matrix4[transforms.Count];
			for(int i = 0; i < transformMatrices.Length; i++)
			{
				transformMatrices[i] = Matrix4.Transpose(transforms[i].GetLocalToWorldMatrix());
			}

			VwBO = GL.GenBuffer();
			GL.BindBuffer(BufferTarget.ArrayBuffer, VwBO);
			GL.BufferData(BufferTarget.ArrayBuffer, transformMatrices.Length * sizeof(float) * 16, transformMatrices, BufferUsageHint.DynamicDraw);	//Note: this should be edited in the future so things can be moved
			
			GL.BindVertexArray(VAO);

			for(int i = 0; i < 4; i++)
			{
				GL.VertexAttribPointer(4+i, 4, VertexAttribPointerType.Float, false, sizeof(float) * 16, sizeof(float) * 4 * i);
				GL.VertexAttribDivisor(4+i, 1);
				GL.EnableVertexAttribArray(4+i);
			}
		}

		public void Draw()
		{
			material.Use();
			material.SetMatrix4x4("worldToClip", Camera.WorldToView * Camera.ViewToClip);

			GL.BindVertexArray(VAO);
			GL.DrawElementsInstanced(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, IntPtr.Zero, transforms.Count);
		}

		public void Draw(Transform transform)
		{
			material.Use();
			material.SetMatrix4x4("world", transform.GetLocalToWorldMatrix() * Camera.WorldToView * Camera.ViewToClip);

			GL.BindVertexArray(VAO);
			GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, IntPtr.Zero);
		}

		public void SimpleDraw()
		{
			material.SimpleUse();
			GL.BindVertexArray(VAO);
			GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, IntPtr.Zero);
		}

		public void UpdateTransform(Transform transform)
		{
			int index = transforms.FindIndex(0, transforms.Count, x => x == transform);

			GL.BindBuffer(BufferTarget.ArrayBuffer, VwBO);
			Matrix4[] matrix = new Matrix4[1] { Matrix4.Transpose(transform.GetLocalToWorldMatrix()) };
			
			GL.BufferSubData(BufferTarget.ArrayBuffer, (IntPtr)(sizeof(float) * 16 * index), sizeof(float) * 16, matrix);
		}
	}

	public class DrawableList : List<Drawable>
	{
		public DrawableList(CMoby moby, CMoby.Bangle bangle)
		{
			this.Capacity = (int)bangle.count;
			for(int i = 0; i < bangle.count; i++)
			{
				this.Add(new Drawable(moby, bangle.meshes[i]));
			}
		}
		public DrawableList(CTie tie)
		{
			this.Capacity = (int)tie.meshes.Length;
			for(int i = 0; i < tie.meshes.Length; i++)
			{
				this.Add(new Drawable(tie, tie.meshes[i]));
			}
		}
		public void AddDrawCall(Transform transform)
		{
			for(int i = 0; i < Count; i++)
			{
				this[i].AddDrawCall(transform);
			}
		}
		public void ConsolidateDrawCalls()
		{
			for(int i = 0; i < Count; i++)
			{
				this[i].ConsolidateDrawCalls();
			}
		}

		public void Draw()
		{
			for(int i = 0; i < Count; i++)
			{
				this[i].Draw();
			}
		}

		public void Draw(Transform transform)
		{
			for(int i = 0; i < Count; i++)
			{
				this[i].Draw(transform);
			}
		}

		public void UpdateTransform(Transform transform)
		{
			for(int i = 0; i < Count; i++)
			{
				this[i].UpdateTransform(transform);
			}
		}

	}

	public class DrawableListList : List<DrawableList>
	{
		public DrawableListList(CMoby moby)
		{
			this.Capacity = (int)moby.bangles.Length;
			for(int i = 0; i < moby.bangles.Length; i++)
			{
				this.Add(new DrawableList(moby, moby.bangles[i]));
			}
		}
		public void AddDrawCall(Transform transform)
		{
			for(int i = 0; i < Count; i++)
			{
				this[i].AddDrawCall(transform);
			}
		}
		public void ConsolidateDrawCalls()
		{
			for(int i = 0; i < Count; i++)
			{
				this[i].ConsolidateDrawCalls();
			}
		}
		public void Draw()
		{
			for(int i = 0; i < Count; i++)
			{
				this[i].Draw();
			}
		}
		public void Draw(Transform transform)
		{
			for(int i = 0; i < Count; i++)
			{
				this[i].Draw(transform);
			}
		}
		public void UpdateTransform(Transform transform)
		{
			for(int i = 0; i < Count; i++)
			{
				this[i].UpdateTransform(transform);
			}
		}
	}
}