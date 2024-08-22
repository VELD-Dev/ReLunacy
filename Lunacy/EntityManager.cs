using System.Linq;

namespace Lunacy
{
	public class EntityManager
	{
		static Lazy<EntityManager> lazy = new Lazy<EntityManager>(() => new EntityManager());

		public static EntityManager Singleton => lazy.Value;
		public bool loadUfrags = false;

		public List<Region> regions = new List<Region>();
		public List<CZone> zones = new List<CZone>();
		public Dictionary<string, List<Entity>> MobyHandles = new Dictionary<string, List<Entity>>();
		public List<List<Entity>> TieInstances = new List<List<Entity>>();
		public List<List<Entity>> TFrags = new List<List<Entity>>();

		internal List<Entity> mobys = new List<Entity>();

		internal List<Drawable> transparentDrawables = new List<Drawable>();
		internal List<Drawable> opaqueDrawables = new List<Drawable>();
		public void LoadGameplay(Gameplay gp)
		{
			for(int i = 0; i < gp.regions.Length; i++)
			{
				regions.Add(gp.regions[i]);
				MobyHandles.Add(gp.regions[i].name, new List<Entity>());
				KeyValuePair<ulong, Region.CMobyInstance>[] mobys = gp.regions[i].mobyInstances.ToArray();
				for(ulong j = 0; j < (ulong)mobys.Length; j++)
				{
					MobyHandles[gp.regions[i].name].Add(new Entity(mobys[j].Value));
				}
				for(int j = 0; j < gp.regions[i].zones.Length; j++)
				{
					if(zones.Contains(gp.regions[i].zones[j])) continue;

					CZone zone = gp.regions[i].zones[j];
					zones.Add(zone);

					TieInstances.Add(new List<Entity>());
					KeyValuePair<ulong, CZone.CTieInstance>[] ties = zone.tieInstances.ToArray();
					for(uint k = 0; k < ties.Length; k++)
					{
						TieInstances.Last().Add(new Entity(ties[k].Value));
					}
					TFrags.Add(new List<Entity>());
					if(loadUfrags)
					{
						for(uint k = 0; k < gp.regions[i].zones[j].ufrags.Length; k++)
						{
							var ufrag = new Entity(gp.regions[i].zones[j].ufrags[k]);
                            TFrags.Last().Add(ufrag);
						}
					}
				}
			}

			AssetManager.Singleton.ConsolidateMobys();
			AssetManager.Singleton.ConsolidateTies();

			ReallocDrawableLists();

			/*for(int i = 0; i < gp.zones.Length; i++)
			{
				TFrags.Add(new List<Entity>());
				if(loadUfrags)
				{
					for(uint j = 0; j < gp.zones[i].ufrags.Length; j++)
					{
						TFrags[i].Add(new Entity(gp.zones[i].ufrags[j]));
					}
				}
			}*/
		}

		private void ReallocEntities()
		{
			foreach(KeyValuePair<string, List<Entity>> region in MobyHandles)
			{
				mobys.AddRange(region.Value);
			}
		}

		private void ReallocDrawableLists()
		{
			transparentDrawables.Clear();
			opaqueDrawables.Clear();

			KeyValuePair<ulong, DrawableListList>[] mobys = AssetManager.Singleton.mobys.ToArray();
			Console.WriteLine($"Reallocating {mobys.Length} mobys");
			for(int i = 0; i < mobys.Length; i++)
			{
				List<DrawableList> drawableLists = mobys[i].Value;
				for(int j = 0; j < drawableLists.Count; j++)
				{
					for(int k = 0; k < drawableLists[j].Count; k++)
					{
						if(drawableLists[j][k].material.asset.renderingMode != CShader.RenderingMode.AlphaBlend)
						{
							opaqueDrawables.Add(drawableLists[j][k]);
						}
						else
						{
							transparentDrawables.Add(drawableLists[j][k]);
						}
					}
				}
			}

            KeyValuePair<ulong, DrawableList>[] ties = AssetManager.Singleton.ties.ToArray();
            Console.WriteLine($"Reallocating {ties.Length} ties");
			for(int i = 0; i < ties.Length; i++)
            {
				List<Drawable> drawables = ties[i].Value;
				for(int j = 0; j < drawables.Count; j++)
				{
					if(drawables[j].material.asset.renderingMode != CShader.RenderingMode.AlphaBlend)
					{
						opaqueDrawables.Add(drawables[j]);
					}
					else
					{
						transparentDrawables.Add(drawables[j]);
					}
				}
			}

			if(loadUfrags)
			{
                foreach (var z in TFrags)
				{
					Console.WriteLine($"Reallocating {ties.Length} ties");
					foreach(var uf in z)
                    {
                        var ufragdrawable = uf.drawable as Drawable;
						if(ufragdrawable == null) continue;
						if(ufragdrawable.material.asset.renderingMode != CShader.RenderingMode.AlphaBlend)
						{
							opaqueDrawables.Add(ufragdrawable);
						}
						else
						{
							transparentDrawables.Add(ufragdrawable);
						}
					}
				}
            }
		}

		public void RenderOpaque()
		{
			for(int i = 0; i < opaqueDrawables.Count; i++)
			{
				opaqueDrawables[i].Draw();
			}
		}
		public void RenderTransparent()
		{
			for(int i = 0; i < transparentDrawables.Count; i++)
			{
				transparentDrawables[i].Draw();
			}
		}
	}

	public class Entity
	{
		public object instance;					//Is either a Region.CMobyInstance or a TieInstance depending on if it's a moby or tie repsectively
		public object drawable;					//Is either a DrawableListList or a DrawableList depending on if it's a moby or tie respectively
		public int id;
		public string name = string.Empty;

		public Transform transform;

		//xyz is pos, w is radius
		public Vector4 boundingSphere;

		public Entity(Region.CMobyInstance mobyInstance)
		{
			instance = mobyInstance;
			drawable = AssetManager.Singleton.mobys[mobyInstance.moby.id];
			transform = new Transform(
						new Vector3(mobyInstance.position.X, mobyInstance.position.Y, mobyInstance.position.Z),
						new Vector3(mobyInstance.rotation.X, mobyInstance.rotation.Y, mobyInstance.rotation.Z),
						Vector3.One * mobyInstance.scale
					);
			name = mobyInstance.name;
			(drawable as DrawableListList).AddDrawCall(transform);
			boundingSphere = new Vector4(Utils.ToOpenTK(mobyInstance.moby.boundingSpherePosition) + transform.position, mobyInstance.moby.boundingSphereRadius * mobyInstance.scale);
		}
		public Entity(CZone.CTieInstance tieInstance)
		{
			instance = tieInstance;
			drawable = AssetManager.Singleton.ties[tieInstance.tie.id];
			transform = new Transform(tieInstance.transformation.ToOpenTK());
			name = tieInstance.name;
			(drawable as DrawableList).AddDrawCall(transform);
			boundingSphere = new Vector4(Utils.ToOpenTK(tieInstance.boundingPosition), tieInstance.boundingRadius);
		}
		public Entity(CZone.UFrag ufrag)
		{
			instance = ufrag;
			drawable = new Drawable(ref ufrag);
			name = $"UFrag_{ufrag.GetTuid():X08}";
			transform = new Transform(ufrag.GetPosition().ToOpenTK(), Vector3.Zero, Vector3.One / (float)255f);

			((Drawable)drawable).AddDrawCall(transform);
			((Drawable)drawable).ConsolidateDrawCalls();
		}

		public void SetPosition(Vector3 position)
		{
			transform.position = position;
			if(drawable is DrawableListList dll) dll.UpdateTransform(transform);
			else if(drawable is DrawableList dl) dl.UpdateTransform(transform);
		}
		public void SetRotation(Vector3 rotation)
		{
			transform.SetRotation(rotation);
			if(drawable is DrawableListList dll) dll.UpdateTransform(transform);
			else if(drawable is DrawableList dl) dl.UpdateTransform(transform);
		}
		public void SetScale(Vector3 scale)
		{
			transform.scale = scale;
			if(drawable is DrawableListList dll) dll.UpdateTransform(transform);
			else if(drawable is DrawableList dl) dl.UpdateTransform(transform);
		}
		public void Draw()
		{
			if(drawable is DrawableListList dll) dll.Draw();
			else if(drawable is DrawableList dl) dl.Draw();
			else if(drawable is Drawable d) d.Draw(transform);
		}
		public bool IntersectsRay(Vector3 dir, Vector3 position)
		{
			Vector3 m = position - boundingSphere.Xyz;
			float b = Vector3.Dot(m, dir);
			float c = Vector3.Dot(m, m) - boundingSphere.W * boundingSphere.W;

			if(c > 0 && b > 0) return false;

			float discriminant = b*b - c;

			return discriminant >= 0;
		}
	}
}