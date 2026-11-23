using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace MellowDesktop {
 public sealed partial class HatTheatre {
    // Each sleeve starts on the exact perimeter of an opening in the animated sheet.
    const int shoulderTop=23, shoulderBottom=34, halfShoulder=4, sleeveBands=16;
    readonly int[][] seams=new int[2][];
    readonly MeshGeometry3D[] sleeveMesh=new MeshGeometry3D[2];
    internal readonly GeometryModel3D[] SleeveModels=new GeometryModel3D[2];
    readonly Point3D[] everydayTip=new Point3D[2];
    static int ClothVertex(int row,int column){return row*77+(column+76)%76;}
    static int ShoulderColumn(int hand){return hand==0?38:0;}

    void BuildSleeves(Material material) {
        var shell=new Int32Collection();
        for(int row=0;row<44;row++)for(int col=0;col<76;col++){
            bool shoulder=row>=shoulderTop && row<shoulderBottom &&
                (col<halfShoulder || col>=76-halfShoulder || (col>=38-halfShoulder && col<38+halfShoulder));
            if(shoulder)continue;
            int a=row*77+col,b=a+77;
            foreach(int n in new[]{a,a+1,b,a+1,b+1,b})shell.Add(n);
        }
        sheet.TriangleIndices=shell;
        for(int i=0;i<2;i++){
            var rim=new List<int>();int col=ShoulderColumn(i),left=col-halfShoulder,right=col+halfShoulder;
            for(int j=left;j<right;j++)rim.Add(ClothVertex(shoulderTop,j));
            for(int r=shoulderTop;r<shoulderBottom;r++)rim.Add(ClothVertex(r,right));
            for(int j=right;j>left;j--)rim.Add(ClothVertex(shoulderBottom,j));
            for(int r=shoulderBottom;r>shoulderTop;r--)rim.Add(ClothVertex(r,left));
            seams[i]=rim.ToArray();
            var mesh=new MeshGeometry3D();sleeveMesh[i]=mesh;
            for(int band=0;band<sleeveBands;band++)for(int j=0;j<rim.Count;j++){
                int a=band*rim.Count+j,b=band*rim.Count+(j+1)%rim.Count;
                mesh.TextureCoordinates.Add(new Point((double)j/rim.Count,(double)band/sleeveBands));
                mesh.TriangleIndices.Add(a);mesh.TriangleIndices.Add(b);
                if(band==sleeveBands-1)mesh.TriangleIndices.Add(sleeveBands*rim.Count);
                else {
                    mesh.TriangleIndices.Add(a+rim.Count);
                    mesh.TriangleIndices.Add(b);mesh.TriangleIndices.Add(b+rim.Count);mesh.TriangleIndices.Add(a+rim.Count);
                }
            }
            mesh.TextureCoordinates.Add(new Point(.5,1));
            SleeveModels[i]=new GeometryModel3D(mesh,material){BackMaterial=material};
            actor.Children.Add(SleeveModels[i]);
        }
    }
    internal void BendSleeve(int hand,Point3D hold,double amount,double overhead) {
        var seam=seams[hand];var cloth=sheet.Positions;
        Vector3D sum=new Vector3D();
        foreach(int k in seam)sum+=(Vector3D)cloth[k];
        var shoulder=(Point3D)(sum/seam.Length);
        double side=hand*2-1;
        var end=everydayTip[hand]+(hold-everydayTip[hand])*amount;
        var delta=end-shoulder;
        var c1=shoulder+new Vector3D(side*(.10+.14*amount),-.055,.045);
        var c2=shoulder+delta*.68+new Vector3D(side*.05,-.11*amount,.07*amount);
        var hook=end+new Vector3D(side*.29,-.07,.02);
        c2+=(hook-c2)*(overhead*amount);
        var axis=end-c2;if(axis.LengthSquared<1e-9)axis=new Vector3D(side,0,0);
        axis.Normalize();
        double capLength=Math.Min(.085,delta.Length*.22);
        var palm=end-axis*capLength;
        var points=new Point3DCollection(seam.Length*sleeveBands+1);
        foreach(int k in seam)points.Add(cloth[k]);
        for(int band=1;band<sleeveBands;band++){
            const int palmBand=sleeveBands-4;
            double t=Math.Min(1,(double)band/palmBand);
            var a=shoulder+(c1-shoulder)*t;var b=c1+(c2-c1)*t;var c=c2+(palm-c2)*t;
            var d=a+(b-a)*t;var e=b+(c-b)*t;var centre=d+(e-d)*t;
            var tangent=e-d;
            double cap=Math.Max(0,(band-palmBand)/4.0)*P*.5;
            if(band>=palmBand){centre=palm+axis*(capLength*Math.Sin(cap));tangent=axis;}
            if(tangent.LengthSquared<1e-12)tangent=axis;
            var across=new Vector3D(-side*tangent.Y,side*tangent.X,0);
            if(across.LengthSquared<1e-12)across=new Vector3D(0,1,0);
            across.Normalize();tangent.Normalize();
            var depth=Vector3D.CrossProduct(tangent,across)*side;
            // The same strip broadens gently at the palm and closes into a rounded point.
            double radius=(1-.54*Smooth(t/.78)+.035*Smooth((t-.72)/.28))*Math.Cos(cap);
            foreach(int k in seam){
                var v=cloth[k]-shoulder;
                var turned=across*v.Y+depth*v.Z;
                double bend=Smooth(t/.32);
                points.Add(centre+(v+(turned-v)*bend)*radius);
            }
        }
        points.Add(end);sleeveMesh[hand].Positions=points;
        var normals=new Vector3DCollection(points.Count);
        for(int j=0;j<seam.Length;j++){
            int k=seam[j],row=k/77,col=k%77;
            var shellNormal=Vector3D.CrossProduct(cloth[ClothVertex(row,col+1)]-cloth[ClothVertex(row,col-1)],
                cloth[ClothVertex(row+1,col)]-cloth[ClothVertex(row-1,col)]);
            shellNormal.Normalize();
            normals.Add(shellNormal);
        }
        for(int band=1;band<sleeveBands;band++)for(int j=0;j<seam.Length;j++){
            int baseIndex=band*seam.Length;
            var shellNormal=normals[j];
            var around=points[baseIndex+(j+1)%seam.Length]-points[baseIndex+(j+seam.Length-1)%seam.Length];
            var next=band==sleeveBands-1?end:points[baseIndex+j+seam.Length];
            var normal=Vector3D.CrossProduct(around,next-points[baseIndex+j-seam.Length]);
            if(normal.LengthSquared>1e-12)normal.Normalize();else normal=shellNormal;
            normals.Add(shellNormal+(normal-shellNormal)*Smooth(band/3.0));
        }
        normals.Add(axis);sleeveMesh[hand].Normals=normals;
        if(hand==1){
            // Scale the umbrella outward from the gripped end of its shaft.
            var prop=new Transform3DGroup();
            prop.Children.Add(new TranslateTransform3D(0,.80,0));
            prop.Children.Add(new ScaleTransform3D(canopyOpen,canopyOpen,canopyOpen));
            prop.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),8*canopyOpen)));
            prop.Children.Add(new TranslateTransform3D(end.X,end.Y,end.Z));
            canopy.Transform=prop;
        }
    }
    internal void VerifySleeves() {
        for(int i=0;i<2;i++){
            var mesh=sleeveMesh[i];var seam=seams[i];
            if(mesh.Positions.Count!=sleeveBands*seam.Length+1)throw new InvalidOperationException("Open sleeve tip.");
            for(int j=0;j<seam.Length;j++)
                if((mesh.Positions[j]-sheet.Positions[seam[j]]).Length>1e-8)
                    throw new InvalidOperationException("Animated sleeve separated at its shoulder.");
            if(i==1 && canopyOpen>.001) {
                var handle=canopy.Transform.Transform(new Point3D(0,-.80,0));
                if((handle-mesh.Positions[mesh.Positions.Count-1]).Length>1e-8)
                    throw new InvalidOperationException("Umbrella grip separated.");
            }
            foreach(var p in mesh.Positions)if(double.IsNaN(p.X+p.Y+p.Z) || double.IsInfinity(p.X+p.Y+p.Z))
                throw new InvalidOperationException("Invalid sleeve deformation.");
        }
    }
 }
}
